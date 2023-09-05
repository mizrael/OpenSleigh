using Confluent.Kafka;

namespace OpenSleigh.Transport.Kafka
{
    public interface IKafkaMessageHandler
    {
        ValueTask HandleAsync(ConsumeResult<string, ReadOnlyMemory<byte>> result, QueueReferences queueReferences, CancellationToken cancellationToken = default);
    }
}