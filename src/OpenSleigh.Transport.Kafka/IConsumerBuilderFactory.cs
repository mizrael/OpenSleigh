using Confluent.Kafka;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Transport.Kafka
{
    public interface IConsumerBuilderFactory
    {
        ConsumerBuilder<TKey, TValue> Create<TM, TKey, TValue>() where TM : IMessage;
    }
}