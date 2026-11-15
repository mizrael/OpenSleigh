using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenSleigh.Outbox;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Collections.Concurrent;

namespace OpenSleigh.Transport.RabbitMQ;

/// <summary>
/// puts a message the broker returned back in the outbox, to be published again after
/// <see cref="RabbitConfiguration.RetryDelay"/>. A message that still comes back when
/// <see cref="RabbitConfiguration.ReturnedMessageRetryPeriod"/> is over is published to its dead letter exchange,
/// and dropped when no queue takes it there either.
/// </summary>
/// <remarks>
/// The broker returns a message some time after its publish completed: by then the relay has removed it from the outbox,
/// or is about to. When a message first came back is remembered here, in memory: the period starts again with the process,
/// and every instance that publishes the message counts its own.
/// </remarks>
internal sealed class ReturnedMessageHandler : IReturnedMessageHandler
{
    private static readonly TimeSpan OutboxRetryInterval = TimeSpan.FromMilliseconds(200);
    private const int MaxOutboxAttempts = 50;
    private const int ReturnsKeptWithoutPruning = 1024;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRabbitMessageParser _messageParser;
    private readonly IQueueReferenceFactory _queueReferenceFactory;
    private readonly RabbitConfiguration _configuration;
    private readonly ILogger<ReturnedMessageHandler> _logger;

    private readonly ConcurrentDictionary<string, DateTimeOffset> _firstReturnByMessageId = new();

    public ReturnedMessageHandler(
        IServiceScopeFactory scopeFactory,
        IRabbitMessageParser messageParser,
        IQueueReferenceFactory queueReferenceFactory,
        RabbitConfiguration configuration,
        ILogger<ReturnedMessageHandler> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _messageParser = messageParser ?? throw new ArgumentNullException(nameof(messageParser));
        _queueReferenceFactory = queueReferenceFactory ?? throw new ArgumentNullException(nameof(queueReferenceFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task HandleAsync(IChannel channel, BasicReturnEventArgs returned)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(returned);

        var messageId = returned.BasicProperties.MessageId;
        try
        {
            // everything that reads the message happens before control is given back: its body is gone afterwards
            var now = DateTimeOffset.UtcNow;
            var envelope = _messageParser.Parse(returned.BasicProperties, returned.Body.Span, deliverAt: now + _configuration.RetryDelay);
            var references = _queueReferenceFactory.Create(envelope);

            if (returned.Exchange == references.DeadLetterExchangeName)
            {
                LogDropped(envelope, returned);
                return;
            }

            if (now - FirstReturnOf(envelope.MessageId, now) < _configuration.ReturnedMessageRetryPeriod)
            {
                LogPutBack(envelope, returned);

                // the channel is waiting for this call to give control back
                await Task.Yield();
                await PutBackAsync(envelope).ConfigureAwait(false);
                return;
            }

            _firstReturnByMessageId.TryRemove(envelope.MessageId, out _);

            var properties = new BasicProperties(returned.BasicProperties);
            var body = returned.Body.ToArray();
            LogDeadLettered(envelope, returned, references);

            await Task.Yield();
            await DeadLetterAsync(channel, references, properties, body).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "message '{MessageId}', returned by the broker from Exchange '{ExchangeName}' with '{ReplyText}', is lost: {Error}",
                messageId, returned.Exchange, returned.ReplyText, ex.Message);
        }
    }

    private void LogPutBack(MessageEnvelope envelope, BasicReturnEventArgs returned)
        => _logger.LogWarning(
            "message '{MessageId}' of type '{MessageType}' was returned by the broker from Exchange '{ExchangeName}' with '{ReplyText}': " +
            "no queue took it. It goes back to the outbox and is published again in {RetryDelay}.",
            envelope.MessageId, envelope.MessageType.FullName, returned.Exchange, returned.ReplyText, _configuration.RetryDelay);

    private void LogDeadLettered(MessageEnvelope envelope, BasicReturnEventArgs returned, QueueReferences references)
        => _logger.LogError(
            "message '{MessageId}' of type '{MessageType}' is still returned by the broker from Exchange '{ExchangeName}' with '{ReplyText}' " +
            "after {RetryPeriod}: no service that consumes it has declared its queue. It is not published again, and goes to " +
            "the dead letter exchange '{DeadLetterExchangeName}'.",
            envelope.MessageId, envelope.MessageType.FullName, returned.Exchange, returned.ReplyText,
            _configuration.ReturnedMessageRetryPeriod, references.DeadLetterExchangeName);

    private void LogDropped(MessageEnvelope envelope, BasicReturnEventArgs returned)
        => _logger.LogError(
            "message '{MessageId}' of type '{MessageType}' was dropped: no queue took it from the dead letter exchange '{ExchangeName}' either " +
            "('{ReplyText}'). This entry is the only trace of it.",
            envelope.MessageId, envelope.MessageType.FullName, returned.Exchange, returned.ReplyText);

    private DateTimeOffset FirstReturnOf(string messageId, DateTimeOffset now)
    {
        if (_firstReturnByMessageId.Count > ReturnsKeptWithoutPruning)
            ForgetMessagesThatStoppedComingBack(now);

        return _firstReturnByMessageId.GetOrAdd(messageId, now);
    }

    // a message that was delivered in the end is never heard of again: nothing says so, but time
    private void ForgetMessagesThatStoppedComingBack(DateTimeOffset now)
    {
        var notExpectedBackSince = now - _configuration.ReturnedMessageRetryPeriod - _configuration.RetryDelay * 4;

        foreach (var (messageId, firstReturn) in _firstReturnByMessageId)
            if (firstReturn < notExpectedBackSince)
                _firstReturnByMessageId.TryRemove(messageId, out _);
    }

    // the relay removes a message from the outbox after it has published it, which can be after the broker
    // has returned it: until then the outbox refuses the message as one it already has
    private async Task PutBackAsync(MessageEnvelope envelope)
    {
        using var scope = _scopeFactory.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();

        for (var attempt = 1; ; attempt++)
        {
            var result = await outbox.AppendAsync([envelope]).ConfigureAwait(false);
            if (result != OutboxAppendResult.Duplicate)
                return;

            if (attempt == MaxOutboxAttempts)
                throw new InvalidOperationException("the outbox still has the message as it was before it was published.");

            await Task.Delay(OutboxRetryInterval).ConfigureAwait(false);
        }
    }

    // the exchange is declared, so that publishing to it cannot close the channel, and no queue is:
    // the dead letter queues belong to the services that consume the message. A message none of them takes comes back
    // here once more, and is dropped
    private async Task DeadLetterAsync(IChannel channel, QueueReferences references, BasicProperties properties, byte[] body)
    {
        await channel.EnsureDeadLetterExchangeAsync(references, _configuration).ConfigureAwait(false);

        await channel.BasicPublishAsync(
            exchange: references.DeadLetterExchangeName,
            routingKey: references.DeadLetterQueue,
            mandatory: true,
            basicProperties: properties,
            body: body).ConfigureAwait(false);
    }
}
