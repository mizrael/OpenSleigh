using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenSleigh.Outbox;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Threading.Channels;

namespace OpenSleigh.Transport.RabbitMQ;

/// <summary>
/// puts the messages the broker returned back in the outbox, to be published again after
/// <see cref="RabbitConfiguration.RetryDelay"/>. A message that still comes back when
/// <see cref="RabbitConfiguration.ReturnedMessageRetryPeriod"/> is over is published to its dead letter exchange,
/// and dropped when no queue takes it there either.
/// </summary>
/// <remarks>
/// When a message first came back is stored with it in the outbox and published with it: nothing is remembered here.
/// The messages are taken over on the channel that returned them and handled one after the other, away from it.
/// </remarks>
internal sealed class ReturnedMessageHandler : IReturnedMessageHandler, IHostedService, IAsyncDisposable
{
    private const int MaxWaitingMessages = 10_000;
    private const int MaxMessagesPerWrite = 100;
    private const int MaxOutboxAttempts = 5;

    private sealed record ReturnedMessage(BasicProperties Properties, byte[] Body, string Exchange, string ReplyText)
    {
        public string? MessageType
            => Properties.Headers is { } headers && headers.TryGetValue(nameof(MessageEnvelope.MessageType), out var value)
                ? value switch
                {
                    byte[] bytes => Encoding.UTF8.GetString(bytes),
                    _ => value?.ToString(),
                }
                : null;
    }

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRabbitMessageParser _messageParser;
    private readonly IQueueReferenceFactory _queueReferenceFactory;
    private readonly IBusConnection _connection;
    private readonly RabbitConfiguration _configuration;
    private readonly ILogger<ReturnedMessageHandler> _logger;
    private readonly TimeProvider _timeProvider;

    private readonly Channel<ReturnedMessage> _waiting = Channel.CreateBounded<ReturnedMessage>(
        new BoundedChannelOptions(MaxWaitingMessages) { SingleReader = true });

    private readonly object _workerGate = new();
    private Task? _worker;

    // only the worker uses it: the publish channel belongs to the publisher
    private IChannel? _deadLetterChannel;

    public ReturnedMessageHandler(
        IServiceScopeFactory scopeFactory,
        IRabbitMessageParser messageParser,
        IQueueReferenceFactory queueReferenceFactory,
        IBusConnection connection,
        RabbitConfiguration configuration,
        ILogger<ReturnedMessageHandler> logger,
        TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _messageParser = messageParser ?? throw new ArgumentNullException(nameof(messageParser));
        _queueReferenceFactory = queueReferenceFactory ?? throw new ArgumentNullException(nameof(queueReferenceFactory));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public void Accept(BasicReturnEventArgs returned)
    {
        ArgumentNullException.ThrowIfNull(returned);

        // the body is only there for the length of this call
        var message = new ReturnedMessage(
            new BasicProperties(returned.BasicProperties),
            returned.Body.ToArray(),
            returned.Exchange,
            returned.ReplyText);

        if (_waiting.Writer.TryWrite(message))
            EnsureWorkerRuns();
        else
            LogLost(message, "too many returned messages are waiting, or the application is stopping");
    }

    private void EnsureWorkerRuns()
    {
        if (_worker is not null)
            return;

        lock (_workerGate)
            _worker ??= Task.Run(HandleWaitingMessagesAsync);
    }

    private async Task HandleWaitingMessagesAsync()
    {
        var messages = new List<ReturnedMessage>(MaxMessagesPerWrite);

        while (await _waiting.Reader.WaitToReadAsync().ConfigureAwait(false))
        {
            messages.Clear();
            while (messages.Count < MaxMessagesPerWrite && _waiting.Reader.TryRead(out var message))
                messages.Add(message);

            try
            {
                await HandleAsync(messages).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // the next messages are still handled
                _logger.LogError(ex, "{Count} messages returned by the broker are lost: {Error}", messages.Count, ex.Message);
            }
        }
    }

    private async Task HandleAsync(IReadOnlyCollection<ReturnedMessage> messages)
    {
        var now = _timeProvider.GetUtcNow();
        var toPutBack = new List<MessageEnvelope>(messages.Count);

        foreach (var message in messages)
        {
            try
            {
                var envelope = _messageParser.Parse(message.Properties, message.Body);

                // a difference of two times, never a time plus the period: any period can be configured
                if (now - (envelope.FirstReturnedAt ?? now) < _configuration.ReturnedMessageRetryPeriod)
                {
                    LogPutBack(message);
                    toPutBack.Add(envelope.Returned(now, now + _configuration.RetryDelay));
                }
                else
                {
                    await DeadLetterAsync(message, envelope).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                LogLost(message, ex.Message, ex);
            }
        }

        if (toPutBack.Count > 0)
            await PutBackAsync(toPutBack).ConfigureAwait(false);
    }

    private async Task PutBackAsync(IReadOnlyCollection<MessageEnvelope> messages)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                if (scope.ServiceProvider.GetRequiredService<IOutboxRepository>() is not IOutboxReturnRepository outbox)
                {
                    LogLost(messages, $"the outbox does not implement {nameof(IOutboxReturnRepository)}");
                    return;
                }

                await outbox.PutBackAsync(messages).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (attempt < MaxOutboxAttempts)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                _logger.LogWarning(
                    ex,
                    "{Count} messages returned by the broker could not be put back in the outbox, trying again in {Delay}: {Error}",
                    messages.Count, delay, ex.Message);

                await Task.Delay(delay).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogLost(messages, ex.Message, ex);
                return;
            }
        }
    }

    // the exchange is declared, so that publishing to it cannot close the channel, and no queue is:
    // the dead letter queues belong to the services that consume the message
    private async Task DeadLetterAsync(ReturnedMessage message, MessageEnvelope envelope)
    {
        var references = _queueReferenceFactory.Create(envelope);

        _logger.LogError(
            "message '{MessageId}' of type '{MessageType}' is still returned by the broker from Exchange '{ExchangeName}' with '{ReplyText}' " +
            "after {RetryPeriod}: no service that consumes it has declared its queue. It is not published again, and goes to " +
            "the dead letter exchange '{DeadLetterExchangeName}'.",
            envelope.MessageId, message.MessageType, message.Exchange, message.ReplyText,
            _configuration.ReturnedMessageRetryPeriod, references.DeadLetterExchangeName);

        var channel = await GetDeadLetterChannelAsync().ConfigureAwait(false);
        await channel.EnsureDeadLetterExchangeAsync(references, _configuration).ConfigureAwait(false);

        await channel.BasicPublishAsync(
            exchange: references.DeadLetterExchangeName,
            routingKey: references.DeadLetterQueue,
            mandatory: true,
            basicProperties: message.Properties,
            body: message.Body).ConfigureAwait(false);
    }

    private async ValueTask<IChannel> GetDeadLetterChannelAsync()
    {
        if (_deadLetterChannel is { IsOpen: true })
            return _deadLetterChannel;

        _deadLetterChannel?.Dispose();
        _deadLetterChannel = await _connection.CreateChannelAsync().ConfigureAwait(false);
        _deadLetterChannel.BasicReturnAsync += OnDeadLetterReturnedAsync;
        return _deadLetterChannel;
    }

    // whatever comes back on this channel was published to a dead letter exchange
    private Task OnDeadLetterReturnedAsync(object sender, BasicReturnEventArgs returned)
    {
        var messageType = new ReturnedMessage(new BasicProperties(returned.BasicProperties), [], returned.Exchange, returned.ReplyText).MessageType;

        _logger.LogError(
            "message '{MessageId}' of type '{MessageType}' was dropped: no queue took it from the dead letter exchange '{ExchangeName}' either " +
            "('{ReplyText}'). This entry is the only trace of it.",
            returned.BasicProperties.MessageId, messageType, returned.Exchange, returned.ReplyText);

        return Task.CompletedTask;
    }

    private void LogPutBack(ReturnedMessage message)
        => _logger.LogWarning(
            "message '{MessageId}' of type '{MessageType}' was returned by the broker from Exchange '{ExchangeName}' with '{ReplyText}': " +
            "no queue took it. It goes back to the outbox and is published again in {RetryDelay}.",
            message.Properties.MessageId, message.MessageType, message.Exchange, message.ReplyText, _configuration.RetryDelay);

    private void LogLost(ReturnedMessage message, string reason, Exception? exception = null)
        => _logger.LogError(
            exception,
            "message '{MessageId}' of type '{MessageType}', returned by the broker from Exchange '{ExchangeName}' with '{ReplyText}', is lost: {Reason}",
            message.Properties.MessageId, message.MessageType, message.Exchange, message.ReplyText, reason);

    private void LogLost(IEnumerable<MessageEnvelope> messages, string reason, Exception? exception = null)
    {
        foreach (var message in messages)
            _logger.LogError(
                exception,
                "message '{MessageId}' of type '{MessageType}', returned by the broker, is lost: it could not be put back in the outbox. {Reason}",
                message.MessageId, message.MessageType.FullName, reason);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// takes no more messages, and waits for the ones it has for as long as the host lets it.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _waiting.Writer.TryComplete();

        if (_worker is not { } worker)
            return;

        try
        {
            await worker.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("the application stopped before every message returned by the broker was put back in the outbox: the others are lost.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _waiting.Writer.TryComplete();

        if (_deadLetterChannel is null)
            return;

        if (_deadLetterChannel.IsOpen)
            await _deadLetterChannel.CloseAsync().ConfigureAwait(false);
        _deadLetterChannel.Dispose();
        _deadLetterChannel = null;
    }
}
