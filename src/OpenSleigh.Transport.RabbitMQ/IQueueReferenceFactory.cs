using OpenSleigh.Core;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Transport.RabbitMQ
{
    public interface IQueueReferenceFactory
    {
        QueueReferences Create<TM>() where TM : IMessage;
        QueueReferences Create(IMessage message);
    }
}