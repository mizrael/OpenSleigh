using OpenSleigh.Transport;

namespace OpenSleigh
{
    public interface ISagaExecutionService
    {
        ValueTask<ISagaExecutionContext> StartExecutionContextAsync<TM>(IMessageContext<TM> messageContext,
            SagaDescriptor descriptor,
            CancellationToken cancellationToken = default) where TM : IMessage;

        ValueTask CommitAsync(
            ISagaExecutionContext context,                      
            CancellationToken cancellationToken = default);
    }
}