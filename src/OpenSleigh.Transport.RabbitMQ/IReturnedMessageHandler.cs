using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OpenSleigh.Transport.RabbitMQ;

/// <summary>
/// told about every message the broker sends back to the publish channel because no queue took it.
/// </summary>
public interface IReturnedMessageHandler
{
    /// <summary>
    /// decides what happens to the message. It is called while <paramref name="channel"/> waits for it:
    /// it reads what it needs of the message before it first gives control back, and it does not throw.
    /// </summary>
    /// <param name="channel">the channel the message was published on, and returned to.</param>
    Task HandleAsync(IChannel channel, BasicReturnEventArgs returned);
}
