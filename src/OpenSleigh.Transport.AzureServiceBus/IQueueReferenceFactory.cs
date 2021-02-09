using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Transport.AzureServiceBus
{
    public interface IQueueReferenceFactory
    {
        QueueReferences Create<TM>() where TM : IMessage;
    }
}