using RabbitMQ.Client;

namespace OpenSleigh.Transport.RabbitMQ
{
    public interface IChannelFactory
    {
        IModel Get(QueueReferences references);
    }
}