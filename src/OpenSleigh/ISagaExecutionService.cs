using OpenSleigh.Transport;

namespace OpenSleigh;

public interface ISagaExecutionService
{
    ValueTask<ISagaInstance > BeginInstanceAsync<TM>(IMessageContext<TM> messageContext,
        SagaDescriptor descriptor,
        CancellationToken cancellationToken = default) where TM : IMessage;

    ValueTask CommitAsync(
        ISagaInstance  context,
        CancellationToken cancellationToken = default);
}