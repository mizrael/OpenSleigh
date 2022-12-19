using OpenSleigh.Outbox;

namespace OpenSleigh.Transport
{
    public interface IMessageProcessor
    {
        ValueTask ProcessAsync(OutboxMessage outboxMessage, CancellationToken cancellationToken = default);
    }
}