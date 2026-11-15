using OpenSleigh.Outbox;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OpenSleigh.Transport.RabbitMQ;

internal interface IRabbitMessageParser
{
    Task<MessageEnvelope> ParseMessageAsync(BasicDeliverEventArgs eventArgs);

    /// <summary>
    /// reads a message as it was published, <see cref="MessageEnvelope.FirstReturnedAt"/> included.
    /// </summary>
    MessageEnvelope Parse(IReadOnlyBasicProperties properties, ReadOnlySpan<byte> body);
}
