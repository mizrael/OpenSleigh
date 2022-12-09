using OpenSleigh.Messaging;

namespace OpenSleigh
{
    public interface ISagaRunner
    {
        ValueTask ProcessAsync<TM>(IMessageContext<TM> messageContext, SagaDescriptor descriptor, CancellationToken cancellationToken = default) where TM : IMessage;
    }
}