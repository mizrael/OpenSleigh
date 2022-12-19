using OpenSleigh.Outbox;

namespace OpenSleigh.Transport.RabbitMQ
{
    public interface IQueueReferenceFactory
    {
        QueueReferences Create(OutboxMessage message);
        QueueReferences Create<TM>() where TM : IMessage;
    }
}