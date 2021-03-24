using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Transport.Kafka
{
    public interface IQueueReferenceFactory
    {
        QueueReferences Create<TM>(TM message = default) where TM : IMessage;
    }
}