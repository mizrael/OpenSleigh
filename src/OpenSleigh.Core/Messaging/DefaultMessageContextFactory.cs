using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Core.Messaging
{
    [ExcludeFromCodeCoverage] //only if the implementation doesn't get more complex
    internal class DefaultMessageContextFactory : IMessageContextFactory
    {
        public IMessageContext<TM> Create<TM>(TM message) where TM : IMessage
        {
            return new DefaultMessageContext<TM>(message);
        }
    }
}