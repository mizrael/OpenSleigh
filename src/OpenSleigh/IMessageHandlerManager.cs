using OpenSleigh.Transport;

namespace OpenSleigh;

public interface IMessageHandlerManager
{
    ValueTask ProcessAsync<TM>(
        ISagaInstance executionContext,
        IMessageContext<TM> messageContext,
        CancellationToken cancellationToken = default) where TM : IMessage;

    /// <summary>
    /// runs the compensation handler the saga has for the message (<see cref="ICompensateMessage{TM}"/>).
    /// </summary>
    /// <param name="executionContext">the saga as it was before the failed handler ran.</param>
    ValueTask CompensateAsync<TM>(
        ISagaInstance executionContext,
        IMessageContext<TM> messageContext,
        MessageFailure failure,
        CancellationToken cancellationToken = default) where TM : IMessage;
}