namespace OpenSleigh.Transport.RabbitMQ
{
    public interface IChannelPool
    {
        PublisherChannelContext Get(QueueReferences references);
        void Return(PublisherChannelContext ctx);
    }
}