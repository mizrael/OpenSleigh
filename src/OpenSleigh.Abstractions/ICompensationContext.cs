using System;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core
{
    public interface ICompensationContext<out TM>
        where TM : IMessage
    {
        IMessageContext<TM> MessageContext { get; }
        Exception Exception { get; }
    }
}