using OpenSleigh.Core;

namespace OpenSleigh.Transport.RabbitMQ
{
    public interface IQueueReferenceFactory
    {
        QueueReferences Create<TM>() where TM : IMessage;
    }
}