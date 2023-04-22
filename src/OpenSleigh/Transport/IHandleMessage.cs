namespace OpenSleigh.Transport
{
    public interface IHandleMessage<TM> where TM : IMessage
    {
        ValueTask HandleAsync(IMessageContext<TM> messageContext, CancellationToken cancellationToken = default);
        ValueTask RollbackAsync(IMessageContext<TM> messageContext, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}