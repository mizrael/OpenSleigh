using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core;
using OpenSleigh.Core.BackgroundServices;
using OpenSleigh.Core.Persistence;

namespace OpenSleigh.Persistence.Mongo
{
    public class MongoMessagePublisherService : IMessagePublisherService
    {
        private readonly IOutboxRepository _outboxRepository;
        private readonly ILogger<MongoMessagePublisherService> _logger;
        private readonly IPublisher _publisher;

        public MongoMessagePublisherService(IOutboxRepository outboxRepository,
            IPublisher bus,
            ILogger<MongoMessagePublisherService> logger)
        {
            _outboxRepository = outboxRepository ?? throw new ArgumentNullException(nameof(outboxRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _publisher = bus ?? throw new ArgumentNullException(nameof(bus));
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await ProcessPendingMessages(cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        private async Task ProcessPendingMessages(CancellationToken cancellationToken)
        {
            var messages = await _outboxRepository.ReadMessagesToProcess(cancellationToken);
          
            foreach (var message in messages)
            {
                try
                {
                    await _publisher.PublishAsync(message, cancellationToken);
                    await _outboxRepository.MarkAsSentAsync(message, cancellationToken);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, $"an error has occurred while processing Saga State outbox: {e.Message}");
                }
            }
        }
    }
}