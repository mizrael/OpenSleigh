namespace OpenSleigh.Core
{
    public interface IStartedBy<in TM> : IHandleMessage<TM>
        where TM : IMessage
    { }
}