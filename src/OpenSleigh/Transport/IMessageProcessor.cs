using OpenSleigh.Outbox;

namespace OpenSleigh.Transport;

public interface IMessageProcessor
{
    ValueTask ProcessAsync(MessageEnvelope outboxMessage, CancellationToken cancellationToken = default);
}