using OpenSleigh.Outbox;
using System.Runtime.ExceptionServices;

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
        List<SagaException>? failures = null;

        foreach (var descriptor in descriptors)
        {
            try
            {
                await runner.ProcessAsync(context, descriptor, cancellationToken)
                            .ConfigureAwait(false);
            }
            catch (SagaException failure)
            {
                // a saga whose handler failed does not keep the others from handling the message
                (failures ??= []).Add(failure);
            }
        }

        ThrowIfAnyFailed(failures);
    }

    // reported once every saga had its turn: the transport sends the message to its dead letter destination
    private static void ThrowIfAnyFailed(List<SagaException>? failures)
    {
        if (failures is null)
            return;

        if (failures.Count == 1)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();

        throw new AggregateException(failures);
    }
}
