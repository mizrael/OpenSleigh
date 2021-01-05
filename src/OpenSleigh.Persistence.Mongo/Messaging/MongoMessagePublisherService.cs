using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Persistence;

namespace OpenSleigh.Persistence.Mongo.Messaging
{
    public class MongoOutboxProcessor : IOutboxProcessor
    {
        private readonly IOutboxRepository _outboxRepository;
        private readonly ILogger<MongoOutboxProcessor> _logger;
        private readonly IPublisher _publisher;

        public MongoOutboxProcessor(IOutboxRepository outboxRepository,
            IPublisher bus,
            ILogger<MongoOutboxProcessor> logger)
        {
            _outboxRepository = outboxRepository ?? throw new ArgumentNullException(nameof(outboxRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _publisher = bus ?? throw new ArgumentNullException(nameof(bus));
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            //TODO: use change stream when available
            
            while (!cancellationToken.IsCancellationRequested)
            {
                await ProcessPendingMessages(cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
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