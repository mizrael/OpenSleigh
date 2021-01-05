using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Transport.RabbitMQ
{
    public interface IPublisherChannelFactory
    {
        PublisherChannelContext Create(IMessage message);
    }
}