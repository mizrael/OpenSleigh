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
    {
        _logger.LogInformation("Processing available outbox messages...");

        await using var transaction = await _transactionManager.StartTransactionAsync(cancellationToken);

        try
        {
            var messages = await _outboxRepository.ReadPendingAsync(cancellationToken);
            foreach (var message in messages)
            {
                if (message is null)
                    continue;

                try
                {
                    _logger.LogInformation("Processing outbox message {MessageId}...", message.MessageId);

                    await _publisher.PublishAsync(message, cancellationToken)
                                    .ConfigureAwait(false);

                    await _outboxRepository.DeleteAsync(message, cancellationToken)
                                           .ConfigureAwait(false);

                    _logger.LogInformation("Outbox message {MessageId} processed.", message.MessageId);
                }
                catch (LockException e)
                {
                    _logger.LogDebug(
                        e,
                        "message '{MessageId}' was already locked by another producer. {Error}",
                        message.MessageId,
                        e.Message);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "an error has occurred while processing Outbox: {Error}", e.Message);
                }
            }
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing outbox messages: {Error}", ex.Message);
            await transaction.RollbackAsync();
        }
    }
}