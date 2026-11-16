using Microsoft.Extensions.Logging;
using OpenSleigh.Persistence;
using OpenSleigh.Transport;

namespace OpenSleigh.Outbox;

public class OutboxProcessor : IOutboxProcessor
{
    private readonly IOutboxRepository _outboxRepository;
    private readonly ILogger<OutboxProcessor> _logger;
    private readonly IPublisher _publisher;
    private readonly ITransactionManager _transactionManager;

    public OutboxProcessor(
        IOutboxRepository outboxRepository,
        IPublisher publisher,
        ILogger<OutboxProcessor> logger,
        ITransactionManager transactionManager)
    {
        _outboxRepository = outboxRepository ?? throw new ArgumentNullException(nameof(outboxRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _transactionManager = transactionManager ?? throw new ArgumentNullException(nameof(transactionManager));
    }

    public async ValueTask ProcessPendingMessagesAsync(CancellationToken cancellationToken = default)
        => await RelayPendingMessagesAsync(cancellationToken).ConfigureAwait(false);

    public async ValueTask<OutboxRelayResult> RelayPendingMessagesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing available outbox messages...");

        var relayed = 0;
        var failed = 0;
        var canBeInterrupted = true;

        await using var transaction = await _transactionManager.StartTransactionAsync(cancellationToken);

        try
        {
            // a repository that can remove a whole pass at once is asked to, after the last message was published
            var batchRepository = _outboxRepository as IOutboxBatchRepository;
            var published = new List<MessageEnvelope>();

            // read once, by the first message that has a due time
            DateTimeOffset? now = null;

            var messages = await _outboxRepository.ReadPendingAsync(cancellationToken);
            foreach (var message in messages)
            {
                if (message is null)
                    continue;

                // what was published so far is still removed below: stopping must not send it again
                if (cancellationToken.IsCancellationRequested)
                    break;

                // a repository that knows nothing about due times returns every message: it stays in the outbox
                if (message.DeliverAt is { } dueTime && dueTime > (now ??= DateTimeOffset.UtcNow))
                {
                    _logger.LogDebug(
                        "Outbox message {MessageId} is not due until {DeliverAt}, it is left in the outbox.",
                        message.MessageId,
                        dueTime);
                    continue;
                }

                try
                {
                    _logger.LogInformation("Processing outbox message {MessageId}...", message.MessageId);

                    await _publisher.PublishAsync(message, cancellationToken)
                                    .ConfigureAwait(false);

                    if (batchRepository is null)
                        await _outboxRepository.DeleteAsync(message, cancellationToken)
                                               .ConfigureAwait(false);
                    else
                        published.Add(message);

                    relayed++;

                    _logger.LogInformation("Outbox message {MessageId} processed.", message.MessageId);
                }
                catch (LockException e)
                {
                    failed++;
                    _logger.LogDebug(
                        e,
                        "message '{MessageId}' was already locked by another producer. {Error}",
                        message.MessageId,
                        e.Message);
                }
                catch (Exception e) when (cancellationToken.IsCancellationRequested)
                {
                    failed++;
                    LogInterruptedByStop(e);
                    break;
                }
                catch (Exception e)
                {
                    failed++;
                    _logger.LogError(e, "an error has occurred while processing Outbox: {Error}", e.Message);
                }
            }

            // not cancellable, like the commit: these messages are published, and leaving them in the outbox sends them twice
            canBeInterrupted = false;
            if (batchRepository is not null && published.Count > 0)
                await batchRepository.DeleteAsync(published, CancellationToken.None)
                                     .ConfigureAwait(false);

            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            if (canBeInterrupted && cancellationToken.IsCancellationRequested)
                LogInterruptedByStop(ex);
            else
                _logger.LogError(ex, "Error processing outbox messages: {Error}", ex.Message);

            await transaction.RollbackAsync();

            // nothing was removed from the outbox: reporting progress would make the relay retry without pause
            return new OutboxRelayResult(Relayed: 0, Failed: Math.Max(1, relayed + failed));
        }

        return new OutboxRelayResult(relayed, failed);
    }

    // called only once the caller, the host that stops, has cancelled the pass. A driver does not always report
    // what it was interrupted in as a cancellation: SQL Server can report a failed batch instead. That cannot be told
    // from a failure that happened while stopping, so it is not an error, and it keeps its details
    private void LogInterruptedByStop(Exception exception)
    {
        if (exception is OperationCanceledException)
            _logger.LogInformation("The relay of the outbox messages was interrupted by a stop: what was not removed stays in the outbox.");
        else
            _logger.LogWarning(
                exception,
                "The relay of the outbox messages failed while it was being stopped, what was not removed stays in the outbox: {Error}",
                exception.Message);
    }
}