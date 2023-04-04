using OpenSleigh.Transport;

namespace OpenSleigh
{
    public interface ISagaExecutionService
    {
        ValueTask<(ISagaExecutionContext? context, string? lockId)> BeginAsync<TM>(IMessageContext<TM> messageContext,
            SagaDescriptor descriptor,
            CancellationToken cancellationToken = default) where TM : IMessage;

        ValueTask CommitAsync<TM>(
            ISagaExecutionContext context, 
            IMessageContext<TM> messageContext, 
            string lockId, 
            CancellationToken cancellationToken = default) where TM : IMessage;
    }
}