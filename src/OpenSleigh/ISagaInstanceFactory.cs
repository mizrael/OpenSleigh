using OpenSleigh.Transport;

namespace OpenSleigh;

public interface ISagaInstanceFactory
{
    ISagaInstance  Create<TM>(
        SagaDescriptor descriptor, 
        IMessageContext<TM> messageContext)
        where TM : IMessage;
}