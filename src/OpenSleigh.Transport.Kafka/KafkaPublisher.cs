using Confluent.Kafka;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Utils;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.Kafka
{
    public class KafkaPublisher : IPublisher
    {
        private readonly IProducer<Guid, byte[]> _producer;
        private readonly ISerializer _serializer;
        private readonly IQueueReferenceFactory _queueReferenceFactory;

        public KafkaPublisher(IProducer<Guid, byte[]> producer, ISerializer serializer, IQueueReferenceFactory queueReferenceFactory)
        {
            _producer = producer ?? throw new ArgumentNullException(nameof(producer));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _queueReferenceFactory = queueReferenceFactory ?? throw new ArgumentNullException(nameof(queueReferenceFactory));
        }

        public Task PublishAsync(IMessage message, CancellationToken cancellationToken = default)
        {
            if (message is null)
                throw new ArgumentNullException(nameof(message));

            return PublishAsyncCore(message, cancellationToken);
        }

        private async Task PublishAsyncCore(IMessage message, CancellationToken cancellationToken)
        {
            var messageType = message.GetType();

            var serialized = await _serializer.SerializeAsync(message, cancellationToken);

            var headers = new Headers
            {
                {HeaderNames.MessageType, Encoding.UTF8.GetBytes(messageType.FullName) }
            };

            var kafkaMessage = new Message<Guid, byte[]>()
            {
                Key = message.Id,
                Value = serialized,
                Headers = headers
            };

            var queueRefs = _queueReferenceFactory.Create((dynamic)message);

            var result = await _producer.ProduceAsync(queueRefs.TopicName, kafkaMessage, cancellationToken);
            if (result.Status == PersistenceStatus.NotPersisted)
                throw new Exception($"unable to publish message '{message.Id}'");
        }
    }
}
