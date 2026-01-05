using Confluent.Kafka;

namespace OpenSleigh.Transport.Kafka;

public interface IConsumerBuilderFactory
{
    ConsumerBuilder<TKey, TValue> Create<TKey, TValue>();
}