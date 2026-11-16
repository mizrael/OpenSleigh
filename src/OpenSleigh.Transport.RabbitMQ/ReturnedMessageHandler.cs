using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenSleigh.Outbox;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Threading.Channels;

namespace OpenSleigh.Transport.RabbitMQ;

/// <summary>
/// puts the messages the broker returned back in the outbox, one by one, to be published again after
/// <see cref="RabbitConfiguration.RetryDelay"/>. A message that still comes back when
/// <see cref="RabbitConfiguration.ReturnedMessageRetryPeriod"/> is over is published to its dead letter exchange instead,
/// and dropped when that fails or when no queue takes it there: it never goes back to the outbox. While the connection
/// to the broker is lost it waits here for it instead.
/// </summary>
/// <remarks>
/// When a message first came back is stored with it in the outbox and published with it: nothing is remembered here.
/// The messages are taken over on the channel that returned them and handled away from it,
/// until the host has stopped everything that publishes.
/// </remarks>
internal sealed class ReturnedMessageHandler : IReturnedMessageHandler, IHostedLifecycleService, IAsyncDisposable
{
    private const int MaxWaitingMessages = 10_000;
    private const long MaxWaitingBytes = 128 * 1024 * 1024;
    private const int MaxMessagesPerPass = 100;
    private const string Stopped = "the application stopped before it was stored";

    /// <summary>a returned message is not published again sooner than this, whatever the retry delay.</summary>
    internal static readonly TimeSpan MinRepublishDelay = TimeSpan.FromSeconds(1);

    // what a message that could not be stored costs the ones behind it, before it goes to the back of the queue
    private static readonly TimeSpan PauseAfterFailedPutBack = TimeSpan.FromSeconds(5);

    private sealed record ReturnedMessage(BasicProperties Properties, byte[] Body, string Exchange, string ReplyText, DateTimeOffset ReturnedAt);

    /// <param name="FirstReturnedAt">from its header when it can be believed, or else when it came back this time.</param>
    private sealed record ParsedMessage(ReturnedMessage Returned, MessageEnvelope Envelope, DateTimeOffset FirstReturnedAt);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRabbitMessageParser _messageParser;
    private readonly IQueueReferenceFactory _queueReferenceFactory;
    private readonly IBusConnection _connection;
    private readonly RabbitConfiguration _configuration;
    private readonly ILogger<ReturnedMessageHandler> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _republishDelay;

    // not bounded by itself: a message that is tried again goes back in, and its place must still be there
    private readonly Channel<ReturnedMessage> _waiting = Channel.CreateUnbounded<ReturnedMessage>(
        new UnboundedChannelOptions { SingleReader = true });

    // the messages that are waiting and the ones that are being handled, and their bodies:
    // a message keeps its place and its room until it is stored, dead-lettered or reported lost
    private int _waitingMessages;
    private long _waitingBytes;

    // cancelled when whoever stops this no longer waits for the messages that are left
    private readonly CancellationTokenSource _givingUp = new();

    // cancelled when no more messages are taken: nothing waits to be tried again from then on.
    // Not disposed: it holds nothing, and the host may still close this once it is disposed
    private readonly CancellationTokenSource _closing = new();
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

        // the room for the body is taken before the body is copied: a flood that cannot be taken must not cost
        // a copy of every body, and callers that come together cannot take more room than there is
        var size = returned.Body.Length;
        if (_closed || !TryReserve(size))
        {
            LogRefused(returned);
            return;
        }

        var waits = false;
        try
        {
            // the body is only there for the length of this call
            var message = new ReturnedMessage(
                new BasicProperties(returned.BasicProperties),
                returned.Body.ToArray(),
                returned.Exchange,
                returned.ReplyText,
                _timeProvider.GetUtcNow());

            waits = _waiting.Writer.TryWrite(message);
        }
        finally
        {
            if (!waits)
                Release(size);
        }

        if (!waits)
            LogRefused(returned);
    }

    // the room is only taken when it is there: a body that does not fit takes none from the ones that do
    private bool TryReserve(int size)
    {
        long waitingBytes;
        do
        {
            waitingBytes = Interlocked.Read(ref _waitingBytes);
            if (waitingBytes + size > MaxWaitingBytes)
                return false;
        }
        while (Interlocked.CompareExchange(ref _waitingBytes, waitingBytes + size, waitingBytes) != waitingBytes);

        if (Interlocked.Increment(ref _waitingMessages) <= MaxWaitingMessages)
            return true;

        Release(size);
        return false;
    }

    private void Release(int size)
    {
        Interlocked.Decrement(ref _waitingMessages);
        Interlocked.Add(ref _waitingBytes, -size);
    }

    private async Task HandleWaitingMessagesAsync()
    {
        var expired = new List<ParsedMessage>();

        while (await _waiting.Reader.WaitToReadAsync().ConfigureAwait(false))
        {
            // the ones whose period is over are kept for the end of the pass: the broker can keep them waiting.
            // The pass ends with a message that waits again: it is not read twice in a pass
            var waitsAgain = false;
            for (var handled = 0; !waitsAgain && handled < MaxMessagesPerPass && _waiting.Reader.TryRead(out var message); handled++)
            {
                var expiredBefore = expired.Count;
                try
                {
                    waitsAgain = await HandleAsync(message, expired, _givingUp.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogLost(message, ex.Message, ex);
                }

                if (!waitsAgain && expired.Count == expiredBefore)
                    Release(message.Body.Length);
            }

            try
            {
                await DeadLetterAsync(expired, _givingUp.Token).ConfigureAwait(false);
            }
            finally
            {
                // they are gone, one way or the other: the ones that wait again were taken out
                foreach (var message in expired)
                    Release(message.Returned.Body.Length);
                expired.Clear();
            }
        }
    }

    /// <returns>whether the message is waiting again, to be put back later.</returns>
    private async Task<bool> HandleAsync(ReturnedMessage returned, List<ParsedMessage> expired, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            LogLost(returned, Stopped);
            return false;
        }

        ParsedMessage message;
        try
        {
            // a time of first return is believed when the message can have come back then:
            // not before it existed, and not after it came back this time
            var envelope = _messageParser.Parse(returned.Properties, returned.Body);
            var firstReturnedAt = envelope.FirstReturnedAt is { } first && first >= envelope.CreatedAt && first <= returned.ReturnedAt
                ? first
                : returned.ReturnedAt;

            // the clock of this host is behind the one that created the message: the outbox keeps the earliest time
            // it is given, and is not given one earlier than the creation of the message
            if (firstReturnedAt < envelope.CreatedAt)
                firstReturnedAt = envelope.CreatedAt;
            message = new ParsedMessage(returned, envelope, firstReturnedAt);
        }
        catch (Exception ex)
        {
            LogLost(returned, $"it cannot be read. {ex.Message}", ex);
            return false;
        }

        // looked at every time the message is handled, the ones it could not be stored included.
        // A difference of two times, never a time plus the period: any period can be configured
        var now = _timeProvider.GetUtcNow();
        if (now - message.FirstReturnedAt >= _configuration.ReturnedMessageRetryPeriod)
        {
            expired.Add(message);
            return false;
        }

        return await PutBackAsync(message, now, cancellationToken).ConfigureAwait(false);
    }

    // a message that cannot be stored now waits again, behind the others, for as long as its period lasts:
    // then it goes to its dead letter exchange, which does not need the outbox
    private async Task<bool> PutBackAsync(ParsedMessage message, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var outbox = scope.ServiceProvider.GetService<IOutboxReturnRepository>();
            if (outbox is null)
            {
                LogLost(message.Returned, $"the outbox cannot take messages back: no {nameof(IOutboxReturnRepository)} is registered");
                return false;
            }

            await outbox.PutBackAsync(message.Envelope.Returned(message.FirstReturnedAt, now + _republishDelay), cancellationToken)
                        .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // once no more messages are taken nothing can wait again: the message is lost now, not after a pause
            if (!_closed)
            {
                _logger.LogWarning(
                    ex,
                    "message '{MessageId}' of type '{MessageType}', returned by the broker, could not be put back in the outbox: {Error}. " +
                    "It is tried again later, until its retry period is over.",
                    message.Envelope.MessageId, message.Envelope.MessageType.FullName, ex.Message);

                if (await PauseAsync().ConfigureAwait(false) && _waiting.Writer.TryWrite(message.Returned))
                    return true;
            }

            LogLost(message.Returned, $"{Stopped}. {ex.Message}", ex);
            return false;
        }

        _logger.LogWarning(
            "message '{MessageId}' of type '{MessageType}' was returned by the broker from Exchange '{ExchangeName}' with '{ReplyText}': " +
            "no queue took it. It is back in the outbox, and is published again in {RetryDelay}.",
            message.Envelope.MessageId, message.Envelope.MessageType.FullName, message.Returned.Exchange, message.Returned.ReplyText,
            _republishDelay);
        return false;
    }

    // ends early, with false, when no more messages are taken
    private async Task<bool> PauseAsync()
    {
        try
        {
            await Task.Delay(PauseAfterFailedPutBack, _timeProvider, _closing.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    // a message whose period is over while the connection to the broker is lost is not lost with it: it waits again,
    // here and not in the outbox, with the ones behind it, and is given to its dead letter exchange once the
    // connection is back. Until every hosted service has stopped: from then on nothing waits
    private async Task DeadLetterAsync(List<ParsedMessage> expired, CancellationToken cancellationToken)
    {
        for (var index = 0; index < expired.Count; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                LogLost(expired.Skip(index), Stopped);
                return;
            }

            // asked before and after: a connection that came back in between says nothing of the attempt
            var connected = _connection.IsConnected;
            try
            {
                if (!connected)
                    throw new InvalidOperationException("the connection to the broker is lost");

                await DeadLetterAsync(expired[index], cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested && !(connected && _connection.IsConnected))
                {
                    var waiting = await WaitAgainAsync(expired, index).ConfigureAwait(false);
                    if (waiting > 0)
                    {
                        LogLost(expired.Skip(index + waiting), Stopped, ex);
                        expired.RemoveRange(index, waiting);
                        return;
                    }
                }

                var reason = cancellationToken.IsCancellationRequested
                    ? Stopped
                    : $"its retry period is over, and it could not be given to its dead letter exchange. {ex.Message}";
                LogLost(expired[index].Returned, reason, ex);
            }
        }
    }

    /// <returns>how many of the messages, from that one on, wait again: none once no more messages are taken.</returns>
    private async Task<int> WaitAgainAsync(IReadOnlyList<ParsedMessage> expired, int from)
    {
        if (!await PauseAsync().ConfigureAwait(false))
            return 0;

        var waiting = 0;
        while (from + waiting < expired.Count && _waiting.Writer.TryWrite(expired[from + waiting].Returned))
            waiting++;

        if (waiting > 0)
            _logger.LogWarning(
                "{Count} message(s) returned by the broker, whose retry period is over, cannot be given to their dead letter exchange: " +
                "the connection to the broker is lost. They wait for it.",
                waiting);

        return waiting;
    }

    // the exchange is declared, so that publishing to it cannot close the channel, and no queue is:
    // the dead letter queues belong to the services that consume the message
    private async Task DeadLetterAsync(ParsedMessage message, CancellationToken cancellationToken)
    {
        var references = _queueReferenceFactory.Create(message.Envelope);

        try
        {
            await PublishAsync(message.Returned, references, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // once more, on a new channel: the one that failed may be closed
            await PublishAsync(message.Returned, references, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogError(
            "message '{MessageId}' of type '{MessageType}' is still returned by the broker from Exchange '{ExchangeName}' with '{ReplyText}' " +
            "after {RetryPeriod}: no service that consumes it has declared its queue. It is not published again: it was given to " +
            "the dead letter exchange '{DeadLetterExchangeName}'.",
            message.Envelope.MessageId, message.Envelope.MessageType.FullName, message.Returned.Exchange, message.Returned.ReplyText,
            _configuration.ReturnedMessageRetryPeriod, references.DeadLetterExchangeName);
    }

    private async Task PublishAsync(ReturnedMessage message, QueueReferences references, CancellationToken cancellationToken)
    {
        try
        {
            if (_deadLetterChannel is not { IsOpen: true })
            {
                DropDeadLetterChannel();
                _deadLetterChannel = await _connection.CreateChannelAsync(cancellationToken).ConfigureAwait(false);
                _deadLetterChannel.BasicReturnAsync += OnDeadLetterReturnedAsync;
            }

            await _deadLetterChannel.EnsureDeadLetterExchangeAsync(references, _configuration, cancellationToken).ConfigureAwait(false);

            await _deadLetterChannel.BasicPublishAsync(
                exchange: references.DeadLetterExchangeName,
                routingKey: references.DeadLetterQueue,
                mandatory: true,
                basicProperties: message.Properties,
                body: message.Body,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // a channel that failed is not used again
            DropDeadLetterChannel();
            throw;
        }
    }

    // disposing a channel closes it
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
        => properties.TryGetHeaderValue(nameof(MessageEnvelope.MessageType), out var messageType) ? messageType : null;

    private void LogRefused(BasicReturnEventArgs returned)
        => _logger.LogError(
            "message '{MessageId}' of type '{MessageType}', returned by the broker from Exchange '{ExchangeName}' with '{ReplyText}', is lost: {Reason}",
            returned.BasicProperties.MessageId, MessageTypeOf(returned.BasicProperties), returned.Exchange, returned.ReplyText,
            _closed
                ? "the application has stopped"
                : $"too many returned messages are already waiting to be stored (at most {MaxWaitingMessages} messages and {MaxWaitingBytes} bytes)");

    private void LogLost(ReturnedMessage message, string reason, Exception? exception = null)
        => _logger.LogError(
            exception,
            "message '{MessageId}' of type '{MessageType}', returned by the broker from Exchange '{ExchangeName}' with '{ReplyText}', is lost: {Reason}",
            message.Properties.MessageId, MessageTypeOf(message.Properties), message.Exchange, message.ReplyText, reason);

    private void LogLost(IEnumerable<ParsedMessage> messages, string reason, Exception? exception = null)
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
        _closing.Cancel();
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

        DropDeadLetterChannel();
        _givingUp.Dispose();
    }
}
