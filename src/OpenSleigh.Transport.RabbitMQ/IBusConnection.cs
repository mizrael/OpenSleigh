using RabbitMQ.Client;

namespace OpenSleigh.Transport.RabbitMQ;

public interface IBusConnection
{
    bool IsConnected { get; }

    Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default);
}