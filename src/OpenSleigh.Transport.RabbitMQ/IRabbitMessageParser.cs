using OpenSleigh.Outbox;
using RabbitMQ.Client.Events;

namespace OpenSleigh.Transport.RabbitMQ;

internal interface IRabbitMessageParser
{
    Task<MessageEnvelope> ParseMessageAsync(BasicDeliverEventArgs eventArgs);
}
