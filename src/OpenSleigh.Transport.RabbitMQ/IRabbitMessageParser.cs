using OpenSleigh.Outbox;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OpenSleigh.Transport.RabbitMQ;

internal interface IRabbitMessageParser
{
    Task<MessageEnvelope> ParseMessageAsync(BasicDeliverEventArgs eventArgs);

    /// <param name="deliverAt">the earliest time at which the relay publishes the message again, null for no delay.</param>
    MessageEnvelope Parse(IReadOnlyBasicProperties properties, ReadOnlySpan<byte> body, DateTimeOffset? deliverAt);
}
