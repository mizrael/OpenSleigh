namespace OpenSleigh.Core
{
    public interface IMessageContext<out TM> where TM : IMessage
    {
        TM Message { get; }
    }
}