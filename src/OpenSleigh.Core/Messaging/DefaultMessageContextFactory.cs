using System;
using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Core.Messaging
{
    [ExcludeFromCodeCoverage] //only if the implementation doesn't get more complex
    internal class DefaultMessageContextFactory : IMessageContextFactory
    {
        private readonly SystemInfo _systemInfo;

        public DefaultMessageContextFactory(SystemInfo systemInfo)
        {
            _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));
        }

        public IMessageContext<TM> Create<TM>(TM message) where TM : IMessage
        {
            return new DefaultMessageContext<TM>(message, _systemInfo);
        }
    }
}