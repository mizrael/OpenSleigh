namespace OpenSleigh.Transport
{
    public interface IMessageSubscriber
    {
        void Start();
        void Stop();
    }

    public interface IMessageSubscriber<TM> : IMessageSubscriber where TM : IMessage { }
}