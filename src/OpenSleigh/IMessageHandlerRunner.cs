using OpenSleigh.Transport;

namespace OpenSleigh
{
    public interface IMessageHandlerRunner
    {
        Task ProcessAsync<TM>(IMessageContext<TM> messageContext, SagaDescriptor descriptor, ISagaExecutionContext executionContext, CancellationToken cancellationToken) where TM : IMessage;
    }
}