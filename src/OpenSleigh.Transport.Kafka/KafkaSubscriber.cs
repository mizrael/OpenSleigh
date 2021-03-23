using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core.Messaging;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.Kafka
{
    public sealed class KafkaSubscriber<TM> : ISubscriber<TM>, IDisposable
        where TM : IMessage
    {
        private IConsumer<Guid, byte[]> _consumer;
        private readonly QueueReferences _queueReferences;
        private readonly ILogger<KafkaSubscriber<TM>> _logger;
        private readonly IMessageParser _messageParser;
        private readonly IMessageProcessor _messageProcessor;
        private readonly IKafkaPublisherExecutor _publisher;

        private bool _started = false;

        public KafkaSubscriber(ConsumerBuilder<Guid, byte[]> builder, 
            IMessageParser messageParser, 
            ILogger<KafkaSubscriber<TM>> logger,
            IQueueReferenceFactory queueReferenceFactory,
            IMessageProcessor messageProcessor,
            IKafkaPublisherExecutor publisher)
        {
            if (builder is null)
                throw new ArgumentNullException(nameof(builder));
            
            if (queueReferenceFactory is null)            
                throw new ArgumentNullException(nameof(queueReferenceFactory));
            
            _consumer = builder.Build();            
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _messageParser = messageParser ?? throw new ArgumentNullException(nameof(messageParser));
            _messageProcessor = messageProcessor ?? throw new ArgumentNullException(nameof(messageProcessor));
            _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
            _queueReferences = queueReferenceFactory.Create<TM>();
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {    
            _consumer.Subscribe(_queueReferences.TopicName);

            _started = true;

            return Task.Run(async () => await ConsumeMessages(cancellationToken), cancellationToken);
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

                    _logger.LogDebug("received message '{MessageId}' from Topic '{Topic}'. Processing...",
                                     message.Id, _queueReferences.TopicName);
                }
                catch(ConsumeException ex) when (ex.Error?.Code == ErrorCode.UnknownTopicOrPart)
                {
                    // noop. seems to be a known issue in the c# Kafka driver
                    // occurring when consumers are started before producers.

                    _logger.LogWarning(ex, "Topic '{Topic}' still not available : {Exception}", _queueReferences.TopicName, ex.Message);
                    continue;
                }
                catch (Exception ex) 
                {
                    _logger.LogError(ex, "an exception has occurred while consuming a message: {Exception}", ex.Message);
                }

                try
                {
                    await _messageProcessor.ProcessAsync((dynamic)message, cancellationToken);
                }
                catch (Exception ex)
                {
                    if(message is null)
                        _logger.LogWarning(ex, "an exception has occurred while consuming a message: {Exception}", ex.Message);
                    else
                    {
                        _logger.LogWarning(ex,
                            "an exception has occurred while consuming message '{MessageId}': {Exception}",
                            message.Id, ex.Message);
                        try
                        {
                            if (!string.IsNullOrWhiteSpace(_queueReferences.DeadLetterTopicName))
                                await _publisher.PublishAsync(message, _queueReferences.DeadLetterTopicName,
                                    additionalHeaders: new[]
                                    {
                                        new Header(HeaderNames.Error, Encoding.UTF8.GetBytes(ex.Message))
                                    },
                                    cancellationToken: cancellationToken);
                        }
                        catch (Exception e)
                        {
                            _logger.LogWarning(ex, "an exception has occurred while publishing message '{MessageId}' to DLQ '{DeadLetterQueue}': {Exception}",
                                message.Id,
                                _queueReferences.DeadLetterTopicName,
                                ex.Message);
                        }
                    }
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
