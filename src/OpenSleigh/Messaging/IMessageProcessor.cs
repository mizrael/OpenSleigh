using OpenSleigh.Outbox;

namespace OpenSleigh.Messaging
{
    public interface IMessageProcessor
    {
        ValueTask ProcessAsync(OutboxMessage outboxMessage, CancellationToken cancellationToken = default);
    }
}