using System;

namespace OpenSleigh.Core
{
    internal class DefaultMessageContext<TM> : IMessageContext<TM>
        where TM : IMessage
    {
        public DefaultMessageContext(TM message)
        {
            Message = message ?? throw new ArgumentNullException(nameof(message));
        }

        public TM Message { get; }
    }
}