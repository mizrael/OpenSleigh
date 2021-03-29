using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core.Messaging;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.Kafka
{
    public class KafkaMessageHandler : IKafkaMessageHandler
    {
        private readonly IMessageParser _messageParser;
        private readonly IMessageProcessor _messageProcessor;
        private readonly IKafkaPublisherExecutor _publisher;
        private readonly ILogger<KafkaMessageHandler> _logger;

        public KafkaMessageHandler(IMessageParser messageParser,
                                    IMessageProcessor messageProcessor,
                                    IKafkaPublisherExecutor publisher,
                                    ILogger<KafkaMessageHandler> logger)
        {
            _messageParser = messageParser;
            _messageProcessor = messageProcessor;
            _publisher = publisher;
            _logger = logger;
        }

        public async Task HandleAsync(ConsumeResult<Guid, byte[]> result, QueueReferences queueReferences, CancellationToken cancellationToken = default)
        {
            var message = Parse(result);
            if (message is not null)
                await Process(message, queueReferences, cancellationToken);
        }

        private IMessage Parse(ConsumeResult<Guid, byte[]> result)
        {
            try
            {
                return _messageParser.Parse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "an exception has occurred while consuming a message: {Exception}", ex.Message);
                return null;
            }
        }
        
        private async Task Process(IMessage message, QueueReferences queueReferences, CancellationToken cancellationToken)
        {
            _logger.LogDebug("received message '{MessageId}' from Topic '{Topic}'. Processing...", 
                             message.Id, queueReferences.TopicName);
            try
            {
                await _messageProcessor.ProcessAsync((dynamic)message, cancellationToken);
            }
            catch (Exception ex)
            {
                await HandleProcessErrors(message, queueReferences, ex, cancellationToken);
            }
        }

        private async Task HandleProcessErrors(IMessage message, QueueReferences queueReferences, Exception ex,
                                                CancellationToken cancellationToken)
        {
            _logger.LogWarning(ex, "an exception has occurred while consuming message '{MessageId}': {Exception}",
                               message.Id, ex.Message);
            
            //TODO: consider adding retry policy, maybe using message headers to store the retry count

            if (string.IsNullOrWhiteSpace(queueReferences.DeadLetterTopicName))
                return;

            try
            {
                _logger.LogWarning("pushing message '{MessageId}' to DLQ '{DeadLetterQueue}' ...",
                                    message.Id, queueReferences.DeadLetterTopicName);

                await _publisher.PublishAsync(message, queueReferences.DeadLetterTopicName,
                    additionalHeaders: new[]
                    {
                        new Header(HeaderNames.Error, Encoding.UTF8.GetBytes(ex.Message))
                    },
                    cancellationToken: cancellationToken);
            }
            catch (Exception dlqEx)
            {
                _logger.LogWarning(dlqEx, "an exception has occurred while publishing message '{MessageId}' to DLQ '{DeadLetterQueue}': {Exception}",
                    message.Id,
                    queueReferences.DeadLetterTopicName,
                    dlqEx.Message);
            }
        }
    }
}
