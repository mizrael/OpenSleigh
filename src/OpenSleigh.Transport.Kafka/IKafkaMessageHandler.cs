using Confluent.Kafka;

namespace OpenSleigh.Transport.Kafka;

public interface IKafkaMessageHandler
{
    ValueTask<bool> HandleAsync(ConsumeResult<string, byte[]> result,  CancellationToken cancellationToken = default);
}