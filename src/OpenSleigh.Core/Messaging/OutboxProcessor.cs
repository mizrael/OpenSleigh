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
        private readonly OutboxProcessorOptions _options;

        public OutboxProcessor(IOutboxRepository outboxRepository,
            IPublisher publisher, 
            OutboxProcessorOptions options,
            ILogger<OutboxProcessor> logger)
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
                    var lockId = await _outboxRepository.BeginProcessingAsync(message, cancellationToken);
                    await _publisher.PublishAsync(message, cancellationToken);
                    await _outboxRepository.MarkAsSentAsync(message, lockId, cancellationToken);
                }
                catch (LockException e)
                {
                    _logger.LogDebug(e, $"message '{message.Id}' was already locked by another consumer. {e.Message}");
                }
                catch (Exception e)
                {
                    _logger.LogError(e, $"an error has occurred while processing Saga State outbox: {e.Message}");
                }
            }
        }
    }
}