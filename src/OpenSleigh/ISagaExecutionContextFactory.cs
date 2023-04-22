using OpenSleigh.Transport;

namespace OpenSleigh
{
    public interface ISagaExecutionContextFactory
    {
        ISagaExecutionContext CreateState<TM>(
            SagaDescriptor descriptor, 
            IMessageContext<TM> messageContext)
            where TM : IMessage;
    }
}