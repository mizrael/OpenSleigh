using System;

namespace OpenSleigh.Messaging
{
    public interface IMessageHandlerFactory
    {
        IHandleMessage<TM> Create<TM>(Type sagaType, object state) where TM : IMessage;
    }
}