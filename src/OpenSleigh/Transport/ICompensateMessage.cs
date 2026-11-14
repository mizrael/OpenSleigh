namespace OpenSleigh.Transport;

/// <summary>
/// implemented by a saga that has something to undo, or to do instead, when its handler for <typeparamref name="TM"/> fails.
/// A saga that does not implement it for a message type has that message dead-lettered when its handler fails.
/// </summary>
public interface ICompensateMessage<TM> where TM : IMessage
{
    /// <summary>
    /// runs once the handler of the message has failed and is not going to be tried again.
    /// The saga state is what it was before the failed handler ran: nothing the handler changed or published is kept.
    /// When this method returns, the state changes it made and the messages it published are stored,
    /// and the message counts as processed. When it throws, nothing of it is stored and the message is dead-lettered:
    /// a compensation is not tried again.
    /// </summary>
    /// <param name="failure">why the handler failed.</param>
    ValueTask CompensateAsync(IMessageContext<TM> messageContext, MessageFailure failure, CancellationToken cancellationToken = default);
}
