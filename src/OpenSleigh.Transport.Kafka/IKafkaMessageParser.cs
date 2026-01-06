using Confluent.Kafka;
using OpenSleigh.Outbox;

namespace OpenSleigh.Transport.Kafka;

public interface IKafkaMessageParser
{
    MessageEnvelope Parse(ConsumeResult<string, byte[]> consumeResult);
}