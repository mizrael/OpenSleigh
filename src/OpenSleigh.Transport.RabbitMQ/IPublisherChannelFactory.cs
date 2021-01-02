using OpenSleigh.Core;

namespace OpenSleigh.Transport.RabbitMQ
{
    public interface IPublisherChannelFactory
    {
        PublisherChannelContext Create(IMessage message);
    }
}