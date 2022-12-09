using Microsoft.Extensions.Logging;
using OpenSleigh.Messaging;

namespace OpenSleigh.Outbox
{
    public class OutboxProcessor : IOutboxProcessor
    {
        private readonly IOutboxRepository _outboxRepository;
        private readonly ILogger<OutboxProcessor> _logger;
        private readonly IPublisher _publisher;

        public OutboxProcessor(IOutboxRepository outboxRepository,
            IPublisher publisher,
            ILogger<OutboxProcessor> logger)
        {
            _outboxRepository = outboxRepository ?? throw new ArgumentNullException(nameof(outboxRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        }

        public async ValueTask ProcessPendingMessagesAsync(CancellationToken cancellationToken = default)
        {
            IEnumerable<OutboxMessage> messages = await _outboxRepository.ReadPendingAsync(cancellationToken).ConfigureAwait(false);

            foreach (var message in messages)
            {
                try
                {
                    string lockId = await _outboxRepository.LockAsync(message, cancellationToken)
                                                           .ConfigureAwait(false);

                    await _publisher.PublishAsync(message, cancellationToken)
                                    .ConfigureAwait(false);

                    await _outboxRepository.DeleteAsync(message, lockId, cancellationToken)
                                           .ConfigureAwait(false);
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
        }
    }
}