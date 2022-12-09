using OpenSleigh.Outbox;

namespace OpenSleigh.Messaging
{
    public interface IPublisher
    {
        ValueTask PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default);
    }
}