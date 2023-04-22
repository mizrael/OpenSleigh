using System;

namespace OpenSleigh.Transport
{
    public interface IMessageHandlerFactory
    {
        IHandleMessage<TM> Create<TM>(ISagaExecutionContext context) where TM : IMessage;
    }
}