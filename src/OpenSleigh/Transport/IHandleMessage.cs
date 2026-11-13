namespace OpenSleigh.Transport;

public interface IHandleMessage<TM> where TM : IMessage
{
    ValueTask HandleAsync(IMessageContext<TM> messageContext, CancellationToken cancellationToken = default);

    /// <summary>
    /// called when <see cref="HandleAsync"/> throws. The messages the handler had published are discarded;
    /// the ones published here are stored with the saga and delivered, and the message is recorded as processed
    /// and sent to the dead letter destination of the transport. Does nothing by default.
    /// </summary>
    ValueTask RollbackAsync(IMessageContext<TM> messageContext, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}