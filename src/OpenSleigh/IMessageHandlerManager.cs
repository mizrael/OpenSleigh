using OpenSleigh.Transport;

namespace OpenSleigh;

public interface IMessageHandlerManager
{
    ValueTask ProcessAsync<TM>(
        ISagaExecutionContext executionContext,
        IMessageContext<TM> messageContext,
        CancellationToken cancellationToken = default) where TM : IMessage;
}