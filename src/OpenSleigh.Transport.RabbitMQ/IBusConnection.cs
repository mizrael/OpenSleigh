using RabbitMQ.Client;

namespace OpenSleigh.Transport.RabbitMQ
{
    public interface IBusConnection
    {
        bool IsConnected { get; }

        IModel CreateChannel();
    }
}