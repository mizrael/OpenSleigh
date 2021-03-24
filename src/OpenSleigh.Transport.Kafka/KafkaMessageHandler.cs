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
            IMessage message = null;

            try
            {
                message = _messageParser.Parse(result);

                _logger.LogDebug("received message '{MessageId}' from Topic '{Topic}'. Processing...",
                                 message.Id, queueReferences.TopicName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "an exception has occurred while consuming a message: {Exception}", ex.Message);
            }

            if (message is null)
                return;

            try
            {
                await _messageProcessor.ProcessAsync((dynamic)message, cancellationToken);
            }
            catch (Exception ex)
            {
                if (message is null)
                    _logger.LogWarning(ex, "an exception has occurred while consuming a message: {Exception}", ex.Message);
                else
                    await PushToDeadLetter(queueReferences, message, ex, cancellationToken);
            }
        }

        private async Task PushToDeadLetter(QueueReferences queueReferences, IMessage message, Exception ex, CancellationToken cancellationToken)
        {
            _logger.LogWarning(ex,
                "an exception has occurred while consuming message '{MessageId}': {Exception}",
                message.Id, ex.Message);
            try
            {
                if (!string.IsNullOrWhiteSpace(queueReferences.DeadLetterTopicName))
                    await _publisher.PublishAsync(message, queueReferences.DeadLetterTopicName,
                        additionalHeaders: new[]
                        {
                            new Header(HeaderNames.Error, Encoding.UTF8.GetBytes(ex.Message))
                        },
                        cancellationToken: cancellationToken);
            }
            catch (Exception dlqEx)
            {
                _logger.LogWarning(ex, "an exception has occurred while publishing message '{MessageId}' to DLQ '{DeadLetterQueue}': {Exception}",
                    message.Id,
                    queueReferences.DeadLetterTopicName,
                    dlqEx.Message);
            }
        }
    }
}
