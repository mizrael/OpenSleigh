namespace OpenSleigh.Transport
{
    public interface IMessageBus
    {
        ValueTask<IMessageContext<TM>> PublishAsync<TM>(TM message, CancellationToken cancellationToken = default) where TM : IMessage;
    }
}