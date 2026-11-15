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
/// The messages are taken over on the channel that returned them and handled one batch after the other, away from it,
/// until the host has stopped everything that publishes.
/// </remarks>
internal sealed class ReturnedMessageHandler : IReturnedMessageHandler, IHostedLifecycleService, IAsyncDisposable
{
    private const int MaxWaitingMessages = 10_000;
    private const int MaxMessagesPerBatch = 100;
    private const int MaxOutboxAttempts = 5;
    private const int MaxDeadLetterAttempts = 2;
    private const string Stopped = "the application stopped before it was stored";

    /// <summary>a returned message is not published again sooner than this, whatever the retry delay.</summary>
    internal static readonly TimeSpan MinRepublishDelay = TimeSpan.FromSeconds(1);

    private sealed record ReturnedMessage(BasicProperties Properties, byte[] Body, string Exchange, string ReplyText, DateTimeOffset ReturnedAt);

    /// <param name="Envelope">the message as it goes back to the outbox.</param>
    private sealed record PutBack(ReturnedMessage Returned, MessageEnvelope Envelope);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRabbitMessageParser _messageParser;
    private readonly IQueueReferenceFactory _queueReferenceFactory;
    private readonly IBusConnection _connection;
    private readonly RabbitConfiguration _configuration;
    private readonly ILogger<ReturnedMessageHandler> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _republishDelay;

    private readonly Channel<ReturnedMessage> _waiting = Channel.CreateBounded<ReturnedMessage>(
        new BoundedChannelOptions(MaxWaitingMessages) { SingleReader = true });

    // cancelled when whoever stops this no longer waits for the messages that are left
    private readonly CancellationTokenSource _givingUp = new();
    private readonly Task _worker;
    private volatile bool _closed;

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
        _republishDelay = configuration.RetryDelay > MinRepublishDelay ? configuration.RetryDelay : MinRepublishDelay;

        // it waits for the first message: there is nothing to start, or to find not started, later
        _worker = Task.Run(HandleWaitingMessagesAsync);
    }

    public void Accept(BasicReturnEventArgs returned)
    {
        ArgumentNullException.ThrowIfNull(returned);

        // looked at before the message is copied: a flood that cannot be taken must not cost a copy of every body
        if (_closed || _waiting.Reader.Count >= MaxWaitingMessages)
        {
            LogRefused(returned);
            return;
        }

        // the body is only there for the length of this call
        var message = new ReturnedMessage(
            new BasicProperties(returned.BasicProperties),
            returned.Body.ToArray(),
            returned.Exchange,
            returned.ReplyText,
            _timeProvider.GetUtcNow());

        if (!_waiting.Writer.TryWrite(message))
            LogRefused(returned);
    }

    private async Task HandleWaitingMessagesAsync()
    {
        var batch = new List<ReturnedMessage>(MaxMessagesPerBatch);

        while (await _waiting.Reader.WaitToReadAsync().ConfigureAwait(false))
        {
            batch.Clear();
            while (batch.Count < MaxMessagesPerBatch && _waiting.Reader.TryRead(out var message))
                batch.Add(message);

            try
            {
                await HandleAsync(batch, _givingUp.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // the next messages are still handled
                foreach (var message in batch)
                    LogLost(message, ex.Message, ex);
            }
        }
    }

    private async Task HandleAsync(IReadOnlyCollection<ReturnedMessage> batch, CancellationToken cancellationToken)
    {
        var toPutBack = new List<PutBack>(batch.Count);
        var expired = new List<PutBack>();

        foreach (var returned in OncePerMessage(batch))
        {
            // a time of first return that is later than the return itself is not believed: the period starts now
            var envelope = returned.Envelope.Returned(returned.Returned.ReturnedAt, returned.Returned.ReturnedAt + _republishDelay);
            var comingBackFor = returned.Returned.ReturnedAt - envelope.FirstReturnedAt!.Value;

            // a difference of two times, never a time plus the period: any period can be configured
            (comingBackFor < _configuration.ReturnedMessageRetryPeriod ? toPutBack : expired).Add(returned with { Envelope = envelope });
        }

        // first: the broker can keep the dead letters waiting for a long time
        await PutBackAsync(toPutBack, cancellationToken).ConfigureAwait(false);

        var notDeadLettered = new List<PutBack>();
        foreach (var message in expired)
            if (!await TryDeadLetterAsync(message, cancellationToken).ConfigureAwait(false))
                notDeadLettered.Add(message);

        // they come back once more, and are given to the dead letter exchange again
        await PutBackAsync(notDeadLettered, cancellationToken).ConfigureAwait(false);
    }

    // a message that several relays published comes back several times: it is handled once, as it first came back
    private IEnumerable<PutBack> OncePerMessage(IEnumerable<ReturnedMessage> batch)
    {
        var parsed = new List<PutBack>();
        foreach (var message in batch)
        {
            try
            {
                parsed.Add(new PutBack(message, _messageParser.Parse(message.Properties, message.Body)));
            }
            catch (Exception ex)
            {
                LogLost(message, $"it cannot be read. {ex.Message}", ex);
            }
        }

        return parsed
            .GroupBy(message => message.Envelope.MessageId)
            .Select(sameMessage => sameMessage.MinBy(message => message.Envelope.FirstReturnedAt ?? message.Returned.ReturnedAt)!);
    }

    private async Task PutBackAsync(IReadOnlyCollection<PutBack> messages, CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
            return;

        var envelopes = messages.Select(message => message.Envelope).ToArray();

        try
        {
            for (var attempt = 1; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    if (scope.ServiceProvider.GetRequiredService<IOutboxRepository>() is not IOutboxReturnRepository outbox)
                    {
                        LogLost(messages, $"the outbox does not implement {nameof(IOutboxReturnRepository)}");
                        return;
                    }

                    await outbox.PutBackAsync(envelopes, cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (Exception ex) when (attempt < MaxOutboxAttempts && !cancellationToken.IsCancellationRequested)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                    _logger.LogWarning(
                        ex,
                        "{Count} messages returned by the broker could not be put back in the outbox, trying again in {Delay}: {Error}",
                        messages.Count, delay, ex.Message);

                    await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogLost(messages, Stopped);
            return;
        }
        catch (Exception ex)
        {
            LogLost(messages, $"it could not be put back in the outbox. {ex.Message}", ex);
            return;
        }

        foreach (var message in messages)
            _logger.LogWarning(
                "message '{MessageId}' of type '{MessageType}' was returned by the broker from Exchange '{ExchangeName}' with '{ReplyText}': " +
                "no queue took it. It is back in the outbox, and is published again at {DeliverAt}.",
                message.Envelope.MessageId, message.Envelope.MessageType.FullName, message.Returned.Exchange, message.Returned.ReplyText,
                message.Envelope.DeliverAt);
    }

    // the exchange is declared, so that publishing to it cannot close the channel, and no queue is:
    // the dead letter queues belong to the services that consume the message
    private async Task<bool> TryDeadLetterAsync(PutBack message, CancellationToken cancellationToken)
    {
        var references = _queueReferenceFactory.Create(message.Envelope);

        for (var attempt = 1; !cancellationToken.IsCancellationRequested; attempt++)
        {
            try
            {
                var channel = await GetDeadLetterChannelAsync(cancellationToken).ConfigureAwait(false);
                await channel.EnsureDeadLetterExchangeAsync(references, _configuration, cancellationToken).ConfigureAwait(false);

                await channel.BasicPublishAsync(
                    exchange: references.DeadLetterExchangeName,
                    routingKey: references.DeadLetterQueue,
                    mandatory: true,
                    basicProperties: message.Returned.Properties,
                    body: message.Returned.Body,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                _logger.LogError(
                    "message '{MessageId}' of type '{MessageType}' is still returned by the broker from Exchange '{ExchangeName}' with '{ReplyText}' " +
                    "after {RetryPeriod}: no service that consumes it has declared its queue. It is not published again: it was given to " +
                    "the dead letter exchange '{DeadLetterExchangeName}'.",
                    message.Envelope.MessageId, message.Envelope.MessageType.FullName, message.Returned.Exchange, message.Returned.ReplyText,
                    _configuration.ReturnedMessageRetryPeriod, references.DeadLetterExchangeName);

                return true;
            }
            catch (Exception ex)
            {
                // a channel that failed is not used again
                DropDeadLetterChannel();

                if (attempt < MaxDeadLetterAttempts && !cancellationToken.IsCancellationRequested)
                    continue;

                _logger.LogWarning(
                    ex,
                    "message '{MessageId}' of type '{MessageType}' could not be given to the dead letter exchange '{DeadLetterExchangeName}': {Error}",
                    message.Envelope.MessageId, message.Envelope.MessageType.FullName, references.DeadLetterExchangeName, ex.Message);

                return false;
            }
        }

        return false;
    }

    private async ValueTask<IChannel> GetDeadLetterChannelAsync(CancellationToken cancellationToken)
    {
        if (_deadLetterChannel is { IsOpen: true } open)
            return open;

        DropDeadLetterChannel();

        var channel = await _connection.CreateChannelAsync(cancellationToken).ConfigureAwait(false);
        channel.BasicReturnAsync += OnDeadLetterReturnedAsync;
        return _deadLetterChannel = channel;
    }

    private void DropDeadLetterChannel()
    {
        var channel = _deadLetterChannel;
        _deadLetterChannel = null;

        try
        {
            channel?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "the channel the dead letters are published on could not be disposed: {Error}", ex.Message);
        }
    }

    // whatever comes back on this channel was published to a dead letter exchange
    private Task OnDeadLetterReturnedAsync(object sender, BasicReturnEventArgs returned)
    {
        _logger.LogError(
            "message '{MessageId}' of type '{MessageType}' was dropped: no queue took it from the dead letter exchange '{ExchangeName}' either " +
            "('{ReplyText}'). This entry is the only trace of it.",
            returned.BasicProperties.MessageId, MessageTypeOf(returned.BasicProperties), returned.Exchange, returned.ReplyText);

        return Task.CompletedTask;
    }

    private static string? MessageTypeOf(IReadOnlyBasicProperties properties)
        => properties.Headers is { } headers && headers.TryGetValue(nameof(MessageEnvelope.MessageType), out var value)
            ? value switch
            {
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                _ => value?.ToString(),
            }
            : null;

    private void LogRefused(BasicReturnEventArgs returned)
        => _logger.LogError(
            "message '{MessageId}' of type '{MessageType}', returned by the broker from Exchange '{ExchangeName}' with '{ReplyText}', is lost: {Reason}",
            returned.BasicProperties.MessageId, MessageTypeOf(returned.BasicProperties), returned.Exchange, returned.ReplyText,
            _closed ? "the application has stopped" : $"{MaxWaitingMessages} returned messages are already waiting to be stored");

    private void LogLost(ReturnedMessage message, string reason, Exception? exception = null)
        => _logger.LogError(
            exception,
            "message '{MessageId}' of type '{MessageType}', returned by the broker from Exchange '{ExchangeName}' with '{ReplyText}', is lost: {Reason}",
            message.Properties.MessageId, MessageTypeOf(message.Properties), message.Exchange, message.ReplyText, reason);

    private void LogLost(IEnumerable<PutBack> messages, string reason, Exception? exception = null)
    {
        foreach (var message in messages)
            LogLost(message.Returned, reason, exception);
    }

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // not here: hosted services stop one after the other, and the relay may still be publishing
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// called once every hosted service has stopped, the relay included: takes no more messages,
    /// and stores the ones it has for as long as the host lets it. The others are reported as lost, one by one.
    /// </summary>
    public async Task StoppedAsync(CancellationToken cancellationToken)
    {
        Close();

        try
        {
            await _worker.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _givingUp.Cancel();
            await _worker.ConfigureAwait(false);
        }
    }

    private void Close()
    {
        _closed = true;
        _waiting.Writer.TryComplete();
    }

    private int _disposed;

    // the container disposes what it resolved under each of the services this is registered as
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        Close();
        _givingUp.Cancel();

        // the worker is done with the channel before it is closed
        await _worker.ConfigureAwait(false);

        if (_deadLetterChannel is { IsOpen: true } channel)
            await channel.CloseAsync().ConfigureAwait(false);
        DropDeadLetterChannel();

        _givingUp.Dispose();
    }
}
