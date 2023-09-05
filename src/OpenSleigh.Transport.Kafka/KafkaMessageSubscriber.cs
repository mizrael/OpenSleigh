﻿using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace OpenSleigh.Transport.Kafka
{
    public record KafkaSubscriberConfig(TimeSpan ConsumeDelay, TimeSpan ConsumeTimeout)
    {
        public static readonly KafkaSubscriberConfig Default = new (TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
    }

    public sealed class KafkaMessageSubscriber<TM> : IMessageSubscriber, IDisposable
        where TM : IMessage
    {
        private readonly QueueReferences _queueReferences;
        private readonly IKafkaMessageHandler _messageHandler;
        private readonly ILogger<KafkaMessageSubscriber<TM>> _logger;
        private readonly KafkaSubscriberConfig _config;
        
        private CancellationTokenSource _stoppingCts;
        private Task _consumerTask;
        private IConsumer<string, ReadOnlyMemory<byte>>? _consumer;

        public KafkaMessageSubscriber(
            IConsumerBuilderFactory builderFactory,
            IQueueReferenceFactory queueReferenceFactory,
            IKafkaMessageHandler messageHandler,
            ILogger<KafkaMessageSubscriber<TM>> logger,
            KafkaSubscriberConfig? config = null)
        {
            if (builderFactory is null)
                throw new ArgumentNullException(nameof(builderFactory));

            if (queueReferenceFactory is null)
                throw new ArgumentNullException(nameof(queueReferenceFactory));

            var builder = builderFactory.Create<TM, string, ReadOnlyMemory<byte>>();
            _consumer = builder.Build();

            _queueReferences = queueReferenceFactory.Create<TM>();
            _messageHandler = messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _config = config ?? KafkaSubscriberConfig.Default;
        }

        public void Start()
        {
            _stoppingCts = new ();

            _consumerTask = Task.Run(async () => await ConsumeMessages(_stoppingCts.Token));
        }

        private async Task ConsumeMessages(CancellationToken stoppingToken)
        {
            _consumer?.Subscribe(_queueReferences.TopicName);

            while (!stoppingToken.IsCancellationRequested)
            {
                var canContinue = await ConsumeMessageAsync(stoppingToken);
                if (!canContinue)
                    break;

                // TODO: check if it's possible to get rid of this
                await Task.Delay(_config.ConsumeDelay, stoppingToken);
            }
        }

        /// <summary>
        /// consumes a single message 
        /// </summary>
        /// <returns>false if consumer loop should be stopped</returns>
        private async Task<bool> ConsumeMessageAsync(CancellationToken stoppingToken)
        {
            try
            {
                var result = _consumer.Consume((int)_config.ConsumeTimeout.TotalMilliseconds);

                // task might have been canceled during the call to Consume()
                if (stoppingToken.IsCancellationRequested)
                    return false;

                var canProcess = (result is not null && !result.IsPartitionEOF);
                if(canProcess)
                    await _messageHandler.HandleAsync(result, _queueReferences, stoppingToken);
                                
                return true;
            }
            catch (ConsumeException ex) when (ex.Error?.Code == ErrorCode.UnknownTopicOrPart)
            {
                // noop. seems to be a known issue in the c# Kafka driver
                // occurring when consumers are started before producers.

                _logger.LogWarning(ex, "Topic '{Topic}' still not available : {Exception}",
                    _queueReferences.TopicName, ex.Message);
                await Task.Delay(_config.ConsumeDelay, stoppingToken);
                return true;
            }
            catch (ObjectDisposedException ex)
            {
                _logger.LogWarning(ex, "consumer closed on Topic '{Topic}', probably during Dispose() call",
                    _queueReferences.TopicName);
                return false;
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogInformation(ex, "requested consumer cancellation on Topic '{Topic}'",
                    _queueReferences.TopicName);
                return false;
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogInformation(ex, "requested consumer cancellation on Topic '{Topic}'",
                    _queueReferences.TopicName);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "an error has occurred while consuming messages from Topic '{Topic}': {Exception}",
                    _queueReferences.TopicName, ex.Message);
            }

            return false;
        }

        public void Stop()
        {
            if (_consumerTask == null)
                return;

            _stoppingCts.Cancel();
            _consumer?.Close();
        }

        public void Dispose()
        {
            _stoppingCts?.Cancel();
            _consumer?.Dispose();
            _consumer = null;
        }
    }
}
