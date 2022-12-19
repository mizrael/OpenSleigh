using System;

namespace OpenSleigh.Transport
{
    public interface IMessageHandlerFactory
    {
        IHandleMessage<TM> Create<TM>(Type sagaType, object state) where TM : IMessage;
    }
}