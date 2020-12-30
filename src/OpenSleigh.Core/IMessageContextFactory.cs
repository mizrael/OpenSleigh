namespace OpenSleigh.Core
{
    public interface IMessageContextFactory
    {
        IMessageContext<TM> Create<TM>(TM message) where TM : IMessage;
    }
}