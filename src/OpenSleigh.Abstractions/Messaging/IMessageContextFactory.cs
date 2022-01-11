namespace OpenSleigh.Core.Messaging
{
    public interface IMessageContextFactory
    {
        IMessageContext<TM> Create<TM>(TM message) where TM : IMessage;
    }
}