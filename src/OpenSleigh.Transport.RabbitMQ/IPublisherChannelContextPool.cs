namespace OpenSleigh.Transport.RabbitMQ
{
    public interface IPublisherChannelContextPool
    {
        PublisherChannelContext Get(QueueReferences references);
        void Return(PublisherChannelContext ctx);
    }
}