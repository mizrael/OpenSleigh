using OpenSleigh.Outbox;

namespace OpenSleigh.Transport;

internal interface IMessageDispatcher
{
    ValueTask DispatchAsync(
        MessageEnvelope envelope,
        ISagaRunner runner,
        SagaDescriptor descriptor,
        CancellationToken cancellationToken);
}

internal sealed class MessageDispatcher<TM> : IMessageDispatcher where TM : IMessage
{
    public async ValueTask DispatchAsync(
        MessageEnvelope envelope,
        ISagaRunner runner,
        SagaDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        var context = DefaultMessageContext<TM>.Create(envelope);
        await runner.ProcessAsync(context, descriptor, cancellationToken)
                    .ConfigureAwait(false);
    }
}
