using OpenSleigh.Outbox;

namespace OpenSleigh.Transport
{
    public interface IPublisher
    {
        ValueTask PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default);
    }
}