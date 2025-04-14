using Confluent.Kafka;
using OpenSleigh.Outbox;

namespace OpenSleigh.Transport.Kafka;

public interface IKafkaPublisherExecutor
{
    ValueTask<DeliveryResult<string, byte[]>> PublishAsync(
        OutboxMessage message, 
        string topic,
        IEnumerable<Header>? additionalHeaders = null,
        CancellationToken cancellationToken = default);
}