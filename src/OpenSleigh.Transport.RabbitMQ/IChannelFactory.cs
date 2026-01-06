using RabbitMQ.Client;

namespace OpenSleigh.Transport.RabbitMQ;

public interface IChannelFactory
{
    ValueTask<IChannel> GetPublishChannelAsync(CancellationToken cancellationToken = default);

    ValueTask<IChannel> GetConsumeChannelAsync(CancellationToken cancellationToken = default);
}