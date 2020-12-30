namespace OpenSleigh.Core
{
    internal class DefaultMessageContextFactory : IMessageContextFactory
    {
        public IMessageContext<TM> Create<TM>(TM message) where TM : IMessage
        {
            return new DefaultMessageContext<TM>(message);
        }
    }
}