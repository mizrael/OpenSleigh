using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core.Messaging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.Kafka
{
    public class KafkaSubscriber<TM> : ISubscriber<TM>, IDisposable
        where TM : IMessage
    {
        private IConsumer<Guid, byte[]> _consumer;
        private readonly QueueReferences _queueReferences;
        private readonly ILogger<KafkaSubscriber<TM>> _logger;
        private readonly IMessageParser _messageParser;
        private readonly IMessageProcessor _messageProcessor;
        
        private bool _started = false;

        public KafkaSubscriber(ConsumerBuilder<Guid, byte[]> builder, 
            IMessageParser messageParser, 
            ILogger<KafkaSubscriber<TM>> logger,
            IQueueReferenceFactory queueReferenceFactory,
            IMessageProcessor messageProcessor)
        {
            if (builder is null)
                throw new ArgumentNullException(nameof(builder));
            
            if (queueReferenceFactory is null)            
                throw new ArgumentNullException(nameof(queueReferenceFactory));
            

            _consumer = builder.Build();            
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _messageParser = messageParser ?? throw new ArgumentNullException(nameof(messageParser));
            _messageProcessor = messageProcessor ?? throw new ArgumentNullException(nameof(messageProcessor));
            _queueReferences = queueReferenceFactory.Create<TM>();
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {    
            _consumer.Subscribe(_queueReferences.TopicName);

            _started = true;

            return Task.Run(async () => await ConsumeMessages(cancellationToken));
        }

        private async Task ConsumeMessages(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!_started)
                    break;

                IMessage message = null;

                try
                {
                    var result = _consumer.Consume(cancellationToken);
                    if (result.IsPartitionEOF)
                        continue;

                    message = _messageParser.Parse(result);

                    _logger.LogDebug("received message '{MessageId}' from Topic '{ExchangeName}'. Processing...",
                                     message.Id, _queueReferences.TopicName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"an exception has occurred while consuming a message: {ex.Message}");
                }

                //TODO: logging
                //TODO: retry/deadlettering
                await _messageProcessor.ProcessAsync((dynamic)message, cancellationToken);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            _started = false;

            _consumer.Unsubscribe();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _started = false;
            _consumer?.Dispose();
            _consumer = null;
        }
    }
}
