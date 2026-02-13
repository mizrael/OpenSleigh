using OpenSleigh.Outbox;

namespace OpenSleigh.Transport;

internal interface IMessageDispatcher
{
    ValueTask DispatchAsync(
        MessageEnvelope envelope,
        ISagaRunner runner,
        IEnumerable<SagaDescriptor> descriptors,
        CancellationToken cancellationToken);
}

internal sealed class MessageDispatcher<TM> : IMessageDispatcher where TM : IMessage
{
    public async ValueTask DispatchAsync(
        MessageEnvelope envelope,
        ISagaRunner runner,
        IEnumerable<SagaDescriptor> descriptors,
        CancellationToken cancellationToken)
    {
        var context = DefaultMessageContext<TM>.Create(envelope);
        foreach (var descriptor in descriptors)
        {
            try
            {
                await runner.ProcessAsync(context, descriptor, cancellationToken)
                            .ConfigureAwait(false);
            }
            catch (SagaException)
            {
                // TODO: send outboxMessage + descriptor to deadletter
            }
        }
    }
}
