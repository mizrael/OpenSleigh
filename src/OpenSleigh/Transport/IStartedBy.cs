namespace OpenSleigh.Transport
{
    public interface IStartedBy<TM> : IHandleMessage<TM>
        where TM : IMessage
    { }
}