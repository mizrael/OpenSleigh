using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Persistence;

namespace OpenSleigh.Persistence.Mongo.Messaging
{
    public record MongoOutboxProcessorOptions(TimeSpan Interval)
    {
        public static readonly MongoOutboxProcessorOptions Default = new MongoOutboxProcessorOptions(TimeSpan.FromSeconds(5));
    }
    
    public class MongoOutboxProcessor : IOutboxProcessor
    {
        private readonly IOutboxRepository _outboxRepository;
        private readonly ILogger<MongoOutboxProcessor> _logger;
        private readonly IPublisher _publisher;
        private readonly MongoOutboxProcessorOptions _options;

        public MongoOutboxProcessor(IOutboxRepository outboxRepository,
            IPublisher publisher, 
            MongoOutboxProcessorOptions options,
            ILogger<MongoOutboxProcessor> logger)
        {
            _outboxRepository = outboxRepository ?? throw new ArgumentNullException(nameof(outboxRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            //TODO: use change stream when available
            
            while (!cancellationToken.IsCancellationRequested)
            {
                await ProcessPendingMessages(cancellationToken);
                
                //TODO: make the delay configurable
                await Task.Delay(_options.Interval, cancellationToken);
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