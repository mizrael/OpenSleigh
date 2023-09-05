using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using OpenSleigh.Outbox;
using OpenSleigh.Utils;

namespace OpenSleigh.Transport.Kafka
{
    public class KafkaPublisherExecutor : IKafkaPublisherExecutor
    {
        private readonly IProducer<string, ReadOnlyMemory<byte>> _producer;
        private readonly ISerializer _serializer;
        private readonly ILogger<KafkaPublisherExecutor> _logger;

        public KafkaPublisherExecutor(IProducer<string, ReadOnlyMemory<byte>> producer, ISerializer serializer, ILogger<KafkaPublisherExecutor> logger)
        {
            _producer = producer ?? throw new ArgumentNullException(nameof(producer));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<DeliveryResult<string, ReadOnlyMemory<byte>>> PublishAsync(OutboxMessage message, 
            string topic,
            IEnumerable<Header>? additionalHeaders = null,
            CancellationToken cancellationToken = default)
        {
            if (message == null) 
                throw new ArgumentNullException(nameof(message));
            if (string.IsNullOrWhiteSpace(topic))
                throw new ArgumentNullException(nameof(topic), "Value cannot be null or whitespace.");

            _logger.LogInformation("pushing message '{MessageId}' to topic '{Topic}' ...",
                                    message.MessageId, topic);

            return PublishAsyncCore(message, topic, additionalHeaders, cancellationToken);
        }

        private async Task<DeliveryResult<string, ReadOnlyMemory<byte>>> PublishAsyncCore(OutboxMessage message, 
            string topic,
            IEnumerable<Header>? additionalHeaders,
            CancellationToken cancellationToken)
        {
            var serialized = _serializer.Serialize(message);

            var headers = new Headers
            {
                { nameof(message.MessageType),  Encoding.UTF8.GetBytes(message.MessageType.FullName) },
                { nameof(message.ParentId),  Encoding.UTF8.GetBytes(message.ParentId ?? string.Empty) },
                { nameof(message.SenderId),  Encoding.UTF8.GetBytes(message.SenderId) },
                { nameof(message.CorrelationId),  Encoding.UTF8.GetBytes(message.CorrelationId) },
                { nameof(message.CreatedAt),  Encoding.UTF8.GetBytes(message.CreatedAt.ToString()) }
            };
            
            if(additionalHeaders is not null)
                foreach(var header in additionalHeaders)
                    headers.Add(header);
            
            var kafkaMessage = new Message<string, ReadOnlyMemory<byte>>()
            {
                Key = message.MessageId,                
                Value = message.Body,
                Headers = headers
            };
            
            var result = await _producer.ProduceAsync(topic, kafkaMessage, cancellationToken);
            return result;
        }
    }
}