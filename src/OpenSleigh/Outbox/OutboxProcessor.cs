using Microsoft.Extensions.Logging;
using OpenSleigh.Transport;

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
            IEnumerable<OutboxMessage> messages;
            try
            {
                messages = await _outboxRepository.ReadPendingAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "an error has occurred while pulling messages from the Outbox: {Error}", ex.Message);
                return;
            }            

            foreach (var message in messages)
            {
                if (message is null) 
                    continue;

                try
                {
                    string lockId = await _outboxRepository.LockAsync(message, cancellationToken)
                                                           .ConfigureAwait(false);

                    await _publisher.PublishAsync(message, cancellationToken)
                                    .ConfigureAwait(false);

                    await _outboxRepository.DeleteAsync(message, lockId, cancellationToken)
                                           .ConfigureAwait(false);

                    _logger.LogInformation("message '{MessageId}' has been published.", message.MessageId);
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