using Confluent.Kafka;
using OpenSleigh.Outbox;

namespace OpenSleigh.Transport.Kafka;

public interface IMessageParser
{
    MessageEnvelope Parse(ConsumeResult<string, byte[]> consumeResult);
}