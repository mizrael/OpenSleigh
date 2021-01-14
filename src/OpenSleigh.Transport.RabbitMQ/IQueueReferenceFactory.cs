using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Transport.RabbitMQ
{
    public interface IQueueReferenceFactory
    {
        QueueReferences Create<TM>(TM message = default) where TM : IMessage;
    }
}