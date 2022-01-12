using System.Collections.Generic;

namespace OpenSleigh.Core.Messaging
{
    public interface IMessageHandlersResolver
    {
        IEnumerable<IHandleMessage<TM>> Resolve<TM>() where TM : IMessage;
    }
}