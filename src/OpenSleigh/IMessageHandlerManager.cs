using OpenSleigh.Transport;

namespace OpenSleigh
{
    public interface IMessageHandlerManager
    {
        ValueTask ProcessAsync<TM>(
            IMessageContext<TM> messageContext,             
            ISagaExecutionContext executionContext, 
            CancellationToken cancellationToken = default) where TM : IMessage;
    }
}