using RabbitMQ.Client.Events;

namespace OpenSleigh.Transport.RabbitMQ;

/// <summary>
/// given every message the broker sends back to the publish channel because no queue took it.
/// </summary>
public interface IReturnedMessageHandler
{
    /// <summary>
    /// takes the message over. The channel waits for this call, and the message is gone when it ends:
    /// it copies what it needs of it and does nothing else, and it does not throw.
    /// </summary>
    void Accept(BasicReturnEventArgs returned);
}
