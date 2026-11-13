using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenSleigh.Transport;
using OpenSleigh.Outbox;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace OpenSleigh.InMemory.Messaging;

public class InMemorySubscriber : IMessageSubscriber, IDisposable
{
    /// <summary>
    /// how many messages can wait to be processed again after a lock conflict.
    /// While that many are waiting, no new message is read: whoever publishes waits for the subscriber.
    /// </summary>
    public const int MaxPendingRetries = 1000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ChannelReader<MessageEnvelope> _reader;
    private readonly ILogger<InMemorySubscriber> _logger;
    private readonly InMemorySubscriberOptions _options;
    private readonly InMemoryDeadLetters? _deadLetters;
    private readonly ConcurrentQueue<PendingRetry> _pendingRetries = new();

    private CancellationTokenSource? _stoppingCts;
    private Task? _consumerTask;
    private bool disposedValue;

    /// <summary>
    /// creates a subscriber that processes every message in its own dependency injection scope,
    /// with its own <see cref="IMessageProcessor"/>: messages are processed several at a time,
    /// and what processes one of them is not built to be shared.
    /// </summary>
    /// <param name="publisher">not used any more: a message that has to be processed again is kept by the subscriber.</param>
    /// <param name="deadLetters">where the messages that could not be processed end up. When missing, they are only logged.</param>
    public InMemorySubscriber(IServiceScopeFactory scopeFactory,
        ChannelReader<MessageEnvelope> reader,
        ILogger<InMemorySubscriber> logger,
        IPublisher publisher,
        InMemorySubscriberOptions? options = null,
        InMemoryDeadLetters? deadLetters = null)
    {
        ArgumentNullException.ThrowIfNull(publisher);

        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? InMemorySubscriberOptions.Defaults;
        _deadLetters = deadLetters;
    }

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _consumerTask = Task.Run(async () => await ConsumeMessagesAsync(_stoppingCts.Token));

        return ValueTask.CompletedTask;
    }

    private async ValueTask ConsumeMessagesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = await NextBatchAsync(cancellationToken).ConfigureAwait(false);
            var tasks = batch.Select(async outboxMessage =>
            {
                try
                {
                    await ProcessInOwnScopeAsync(outboxMessage, cancellationToken).ConfigureAwait(false);
                }
                // only a stop ends the loop: any other cancellation, a timeout for instance, is a failure of that message
                catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    HandleFailure(outboxMessage, e);
                }
            }).ToArray();

            await Task.WhenAll(tasks);
        }
    }

    private ValueTask<IEnumerable<MessageEnvelope>> NextBatchAsync(CancellationToken cancellationToken)
        => _pendingRetries.IsEmpty
            ? _reader.ReadMultipleAsync(_options.MaxMessagesBatchSize, cancellationToken)
            : NextBatchWithRetriesAsync(cancellationToken);

    private async ValueTask<IEnumerable<MessageEnvelope>> NextBatchWithRetriesAsync(CancellationToken cancellationToken)
    {
        var batch = new List<MessageEnvelope>(_options.MaxMessagesBatchSize);
        TakeDueRetries(batch);

        var canReadNewMessages = _pendingRetries.Count < MaxPendingRetries;
        while (canReadNewMessages && batch.Count < _options.MaxMessagesBatchSize && _reader.TryRead(out var message))
            batch.Add(message);

        if (batch.Count == 0)
            await WaitForNewMessageOrDueRetryAsync(canReadNewMessages, cancellationToken).ConfigureAwait(false);

        return batch;
    }

    // the retries all wait for the same delay, so the first in the queue is the first to be due
    private void TakeDueRetries(List<MessageEnvelope> batch)
    {
        var now = DateTimeOffset.UtcNow;
        while (batch.Count < _options.MaxMessagesBatchSize
               && _pendingRetries.TryPeek(out var retry)
               && retry.DueAt <= now
               && _pendingRetries.TryDequeue(out retry))
            batch.Add(retry.Message);
    }

    private async ValueTask WaitForNewMessageOrDueRetryAsync(bool canReadNewMessages, CancellationToken cancellationToken)
    {
        if (!_pendingRetries.TryPeek(out var next))
            return;

        var timeToNextRetry = next.DueAt - DateTimeOffset.UtcNow;
        if (timeToNextRetry <= TimeSpan.Zero)
            return;

        if (!canReadNewMessages)
        {
            await Task.Delay(timeToNextRetry, cancellationToken).ConfigureAwait(false);
            return;
        }

        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        waiting.CancelAfter(timeToNextRetry);
        try
        {
            await _reader.WaitToReadAsync(waiting.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // no new message in the meantime: a retry is due
        }
    }

    private void HandleFailure(MessageEnvelope message, Exception error)
    {
        if (error.IsLockConflict())
        {
            _logger.LogWarning(error,
                "message '{MessageId}' could not be processed because of a lock conflict, it is going to be processed again: {Error}",
                message.MessageId,
                error.Message);

            // kept aside: the messages that follow are not kept waiting for this one
            _pendingRetries.Enqueue(new PendingRetry(message, DateTimeOffset.UtcNow + _options.RetryDelay));
            return;
        }

        _logger.LogError(error,
            "an exception has occurred while processing message '{MessageId}', it is not going to be processed again: {Error}",
            message.MessageId,
            error.Message);
        _deadLetters?.Add(message, error);
    }

    private async ValueTask ProcessInOwnScopeAsync(MessageEnvelope message, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var messageProcessor = scope.ServiceProvider.GetRequiredService<IMessageProcessor>();
        await messageProcessor.ProcessAsync(message, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _stoppingCts?.Cancel();
            _stoppingCts = null;
        }
        finally
        {
            if (_consumerTask is not null)
                await Task.WhenAny(_consumerTask, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
            _consumerTask = null;
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                _stoppingCts?.Cancel();
                _stoppingCts?.Dispose();
                _stoppingCts = null;
                _consumerTask = null;
            }

            disposedValue = true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private readonly record struct PendingRetry(MessageEnvelope Message, DateTimeOffset DueAt);
}
