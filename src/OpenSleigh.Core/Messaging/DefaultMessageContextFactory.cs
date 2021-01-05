namespace OpenSleigh.Core.Messaging
{
    internal class DefaultMessageContextFactory : IMessageContextFactory
    {
        public IMessageContext<TM> Create<TM>(TM message) where TM : IMessage
        {
            return new DefaultMessageContext<TM>(message);
        }
    }
}