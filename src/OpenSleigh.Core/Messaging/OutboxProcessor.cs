using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core.Exceptions;
using OpenSleigh.Core.Persistence;

namespace OpenSleigh.Core.Messaging
{
    public record OutboxProcessorOptions(TimeSpan Interval)
    {
        public static readonly OutboxProcessorOptions Default = new (TimeSpan.FromSeconds(5));
    }
    
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

        public async Task ProcessPendingMessagesAsync(CancellationToken cancellationToken = default)
        {
            var messages = await _outboxRepository.ReadMessagesToProcess(cancellationToken);

            foreach (var message in messages)
            {
                try
                {
                    var lockId = await _outboxRepository.LockAsync(message, cancellationToken);
                    await _publisher.PublishAsync(message, cancellationToken);
                    await _outboxRepository.ReleaseAsync(message, lockId, cancellationToken);
                }
                catch (LockException e)
                {
                    _logger.LogDebug(e, $"message '{message.Id}' was already locked by another producer. {e.Message}");
                }
                catch (Exception e)
                {
                    _logger.LogError(e, $"an error has occurred while processing Saga State outbox: {e.Message}");
                }
            }
        }
    }
}