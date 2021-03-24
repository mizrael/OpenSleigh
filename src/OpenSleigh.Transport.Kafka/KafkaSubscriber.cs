using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core.Messaging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.Kafka
{
    public sealed class KafkaSubscriber<TM> : ISubscriber<TM>, IDisposable
        where TM : IMessage
    {
        private IConsumer<Guid, byte[]> _consumer;
        private readonly QueueReferences _queueReferences;
        private IKafkaMessageHandler _messageHandler;
        private ILogger<KafkaSubscriber<TM>> _logger;

        private bool _started = false;

        public KafkaSubscriber(ConsumerBuilder<Guid, byte[]> builder,
            IQueueReferenceFactory queueReferenceFactory,
            IKafkaMessageHandler messageHandler,
            ILogger<KafkaSubscriber<TM>> logger)
        {
            if (builder is null)
                throw new ArgumentNullException(nameof(builder));

            if (queueReferenceFactory is null)
                throw new ArgumentNullException(nameof(queueReferenceFactory));

            _consumer = builder.Build();
            _queueReferences = queueReferenceFactory.Create<TM>();
            _messageHandler = messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {    
            _consumer.Subscribe(_queueReferences.TopicName);

            _started = true;

            return Task.Run(async () => await ConsumeMessages(cancellationToken));
        }

        private async Task ConsumeMessages(CancellationToken cancellationToken)
        {
            while (_started && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var result = _consumer.Consume(10);
                    if (result is null || result.IsPartitionEOF)
                        continue;

                    await _messageHandler.HandleAsync(result, _queueReferences, cancellationToken);
                }
                catch (ConsumeException ex) when (ex.Error?.Code == ErrorCode.UnknownTopicOrPart)
                {
                    // noop. seems to be a known issue in the c# Kafka driver
                    // occurring when consumers are started before producers.

                    _logger.LogWarning(ex, "Topic '{Topic}' still not available : {Exception}", _queueReferences.TopicName, ex.Message);
                }
                catch(TaskCanceledException ex)
                {
                    _logger.LogInformation(ex, "requested consumer cancellation on Topic '{Topic}'", _queueReferences.TopicName);
                    break;
                }
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
