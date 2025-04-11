using RabbitMQ.Client;

namespace OpenSleigh.Transport.RabbitMQ;

public interface IChannelFactory
{
    ValueTask<IChannel> GetAsync(QueueReferences references, CancellationToken cancellationToken = default);
}