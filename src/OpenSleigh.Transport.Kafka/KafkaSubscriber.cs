using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core.Messaging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.Kafka
{
    public record KafkaSubscriberConfig(TimeSpan ConsumeDelay)
    {
        public static readonly KafkaSubscriberConfig Default = new (TimeSpan.FromMilliseconds(250));
    }

    public sealed class KafkaSubscriber<TM> : ISubscriber<TM>, IDisposable
        where TM : IMessage
    {
        private IConsumer<Guid, byte[]> _consumer;
        private readonly QueueReferences _queueReferences;
        private readonly IKafkaMessageHandler _messageHandler;
        private readonly ILogger<KafkaSubscriber<TM>> _logger;
        private readonly KafkaSubscriberConfig _config;
        
        private bool _started = false;

        public KafkaSubscriber(ConsumerBuilder<Guid, byte[]> builder,
            IQueueReferenceFactory queueReferenceFactory,
            IKafkaMessageHandler messageHandler,
            ILogger<KafkaSubscriber<TM>> logger,
            KafkaSubscriberConfig config = null)
        {
            if (builder is null)
                throw new ArgumentNullException(nameof(builder));

            if (queueReferenceFactory is null)
                throw new ArgumentNullException(nameof(queueReferenceFactory));

            _consumer = builder.Build();
            _queueReferences = queueReferenceFactory.Create<TM>();
            _messageHandler = messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _config = config ?? KafkaSubscriberConfig.Default;
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            return Task.Run(() => ConsumeMessages(cancellationToken), CancellationToken.None);
        }

        private async Task ConsumeMessages(CancellationToken cancellationToken)
        {
            _started = true;
            _consumer.Subscribe(_queueReferences.TopicName);

            while (_started && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var result = _consumer.Consume(cancellationToken);
                    if (result is null || result.IsPartitionEOF)
                    {
                        await Task.Delay(_config.ConsumeDelay, cancellationToken);
                        continue;
                    }

                    await _messageHandler.HandleAsync(result, _queueReferences, cancellationToken);
                }
                catch (ConsumeException ex) when (ex.Error?.Code == ErrorCode.UnknownTopicOrPart)
                {
                    // noop. seems to be a known issue in the c# Kafka driver
                    // occurring when consumers are started before producers.

                    _logger.LogWarning(ex, "Topic '{Topic}' still not available : {Exception}",
                        _queueReferences.TopicName, ex.Message);
                    await Task.Delay(_config.ConsumeDelay, cancellationToken);
                }
                catch (TaskCanceledException ex)
                {
                    _logger.LogInformation(ex, "requested consumer cancellation on Topic '{Topic}'",
                        _queueReferences.TopicName);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "an error has occurred while consuming messages from Topic '{Topic}': {Exception}",
                        _queueReferences.TopicName, ex.Message);
                }
            }

            _consumer.Unsubscribe();
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            _started = false;
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
