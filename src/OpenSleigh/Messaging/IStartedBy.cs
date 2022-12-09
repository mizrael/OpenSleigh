namespace OpenSleigh.Messaging
{
    public interface IStartedBy<TM> : IHandleMessage<TM>
        where TM : IMessage
    { }
}