using RabbitMQ.Client;

namespace OpenSleigh.Transport.RabbitMQ
{
    public interface IChannelPool
    {
        IModel Get(QueueReferences references);
        void Return(IModel channel, QueueReferences queueReferences);
    }
}