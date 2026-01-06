using OpenSleigh.Outbox;

namespace OpenSleigh.Transport.RabbitMQ;

public interface IQueueReferenceFactory
{
    QueueReferences Create(MessageEnvelope message);
    QueueReferences Create<TM>() where TM : IMessage;
    QueueReferences Create(Type messageType);
}