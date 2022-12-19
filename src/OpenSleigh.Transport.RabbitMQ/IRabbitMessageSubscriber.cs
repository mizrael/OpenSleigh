namespace OpenSleigh.Transport.RabbitMQ
{
    public interface IRabbitMessageSubscriber
    {
        void Start();
        void Stop();
    }

    public interface IRabbitMessageSubscriber<TM> : IRabbitMessageSubscriber where TM : IMessage { }
}