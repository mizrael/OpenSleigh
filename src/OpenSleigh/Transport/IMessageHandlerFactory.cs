namespace OpenSleigh.Transport;

public interface IMessageHandlerFactory
{
    IHandleMessage<TM> Create<TM>(ISagaInstance  context) where TM : IMessage;
}