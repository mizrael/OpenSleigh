using Confluent.Kafka;
using OpenSleigh.Core.Messaging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.Kafka
{
    public class KafkaPublisher : IPublisher
    {
        private readonly IKafkaPublisherExecutor _executor;
        private readonly IQueueReferenceFactory _queueReferenceFactory;

        public KafkaPublisher(IKafkaPublisherExecutor executor, IQueueReferenceFactory queueReferenceFactory)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
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
            var queueRefs = _queueReferenceFactory.Create((dynamic)message);
            var result = await _executor.PublishAsync(message, queueRefs.TopicName, cancellationToken: cancellationToken);
            if (result is null || result.Status == PersistenceStatus.NotPersisted)
                throw new InvalidOperationException($"unable to publish message '{message.Id}'");
        }
    }
}
