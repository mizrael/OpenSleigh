using OpenSleigh.Outbox;

namespace OpenSleigh.Transport.Kafka;

public interface IQueueReferenceFactory
{
    QueueReferences Create(MessageEnvelope message);

    QueueReferences Create<TM>() where TM : IMessage;

    QueueReferences Create(Type messageType);

    QueueReferences? Get(string topic);

    Type GetQueueType(string topic);
}