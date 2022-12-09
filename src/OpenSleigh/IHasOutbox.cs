using OpenSleigh.Outbox;

namespace OpenSleigh
{
    public interface IHasOutbox
    {
        ValueTask PersistOutboxAsync(IOutboxRepository outboxRepository, CancellationToken cancellationToken = default);
    }
}