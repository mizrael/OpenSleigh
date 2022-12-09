namespace OpenSleigh.Messaging
{
    public interface IMessageBus
    {
        ValueTask PublishAsync<TM>(TM message, CancellationToken cancellationToken = default) where TM : IMessage;
    }
}