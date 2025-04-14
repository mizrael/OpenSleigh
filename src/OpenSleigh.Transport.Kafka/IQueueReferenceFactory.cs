using OpenSleigh.Outbox;

namespace OpenSleigh.Transport.Kafka;

public interface IQueueReferenceFactory
{
    QueueReferences Create(OutboxMessage message);

    QueueReferences Create<TM>() where TM : IMessage;

    Type GetQueueType(string topic);
}