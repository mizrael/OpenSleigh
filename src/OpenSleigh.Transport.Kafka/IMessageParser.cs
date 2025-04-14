using Confluent.Kafka;
using OpenSleigh.Outbox;

namespace OpenSleigh.Transport.Kafka;

public interface IMessageParser
{
    OutboxMessage Parse(ConsumeResult<string, byte[]> consumeResult);
}