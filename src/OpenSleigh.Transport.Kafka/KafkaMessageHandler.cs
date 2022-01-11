using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core.Messaging;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core;

namespace OpenSleigh.Transport.Kafka
{
    public class KafkaMessageHandler : IKafkaMessageHandler
    {
        private readonly IMessageParser _messageParser;
        private readonly IMessageProcessor _messageProcessor;
        private readonly IKafkaPublisherExecutor _publisher;
        private readonly ILogger<KafkaMessageHandler> _logger;
        private readonly ISystemInfo _systemInfo;

        public KafkaMessageHandler(IMessageParser messageParser,
                                    IMessageProcessor messageProcessor,
                                    IKafkaPublisherExecutor publisher,
                                    ILogger<KafkaMessageHandler> logger, 
                                    ISystemInfo systemInfo)
        {
            _messageParser = messageParser ?? throw new ArgumentNullException(nameof(messageParser));
            _messageProcessor = messageProcessor ?? throw new ArgumentNullException(nameof(messageProcessor));
            _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));
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
            _logger.LogInformation("client {ClientGroup}/{ClientId} received message '{MessageId}' from Topic '{Topic}'. Processing...", 
                _systemInfo.ClientGroup, _systemInfo.ClientId, 
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

            //await RePublishAsync(message, queueReferences, cancellationToken);
            await PublishToDLQAsync(message, queueReferences, ex, cancellationToken);
        }
        
        private async Task RePublishAsync(IMessage message, QueueReferences queueReferences, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogWarning("republishing message '{MessageId}' to topic '{Topic}' ...",
                    message.Id, queueReferences.TopicName);

                await _publisher.PublishAsync(message, queueReferences.TopicName, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "an exception has occurred while publishing message '{MessageId}' to topic '{Topic}': {Exception}",
                    message.Id,
                    queueReferences.TopicName,
                    ex.Message);
            }
        }

        private async Task PublishToDLQAsync(IMessage message, QueueReferences queueReferences, Exception ex,
            CancellationToken cancellationToken)
        {
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
                _logger.LogWarning(dlqEx,
                    "an exception has occurred while publishing message '{MessageId}' to DLQ '{DeadLetterQueue}': {Exception}",
                    message.Id,
                    queueReferences.DeadLetterTopicName,
                    dlqEx.Message);
            }
        }
    }
}
