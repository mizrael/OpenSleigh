using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Utils;

namespace OpenSleigh.Transport.Kafka
{
    public class KafkaPublisherExecutor : IKafkaPublisherExecutor
    {
        private readonly IProducer<Guid, byte[]> _producer;
        private readonly ITransportSerializer _serializer;
        private readonly ILogger<KafkaPublisherExecutor> _logger;

        public KafkaPublisherExecutor(IProducer<Guid, byte[]> producer, ITransportSerializer serializer, ILogger<KafkaPublisherExecutor> logger)
        {
            _producer = producer ?? throw new ArgumentNullException(nameof(producer));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<DeliveryResult<Guid, byte[]>> PublishAsync(IMessage message, 
            string topic,
            IEnumerable<Header> additionalHeaders = null,
            CancellationToken cancellationToken = default)
        {
            if (message == null) 
                throw new ArgumentNullException(nameof(message));
            if (string.IsNullOrWhiteSpace(topic))
                throw new ArgumentNullException(nameof(topic), "Value cannot be null or whitespace.");

            _logger.LogInformation("pushing message '{MessageId}' to topic '{Topic}' ...",
                                    message.Id, topic);

            return PublishAsyncCore(message, topic, additionalHeaders, cancellationToken);
        }

        private async Task<DeliveryResult<Guid, byte[]>> PublishAsyncCore(IMessage message, 
            string topic,
            IEnumerable<Header> additionalHeaders,
            CancellationToken cancellationToken)
        {
            var messageType = message.GetType();

            var serialized = _serializer.Serialize(message);

            var headers = new Headers
            {
                {HeaderNames.MessageType, Encoding.UTF8.GetBytes(messageType.FullName) }
            };
            
            if(additionalHeaders is not null)
                foreach(var header in additionalHeaders)
                    headers.Add(header);
            
            var kafkaMessage = new Message<Guid, byte[]>()
            {
                Key = message.Id,
                Value = serialized,
                Headers = headers
            };
            
            var result = await _producer.ProduceAsync(topic, kafkaMessage, cancellationToken);
            return result;
        }
    }
}