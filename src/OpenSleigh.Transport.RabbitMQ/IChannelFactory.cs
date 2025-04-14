using RabbitMQ.Client;

namespace OpenSleigh.Transport.RabbitMQ;

public interface IChannelFactory
{
    ValueTask<IChannel> GetAsync(QueueReferences queueReferences, CancellationToken cancellationToken = default);
}