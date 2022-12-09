namespace OpenSleigh.Outbox
{
    public interface IOutboxRepository
    {
        ValueTask AppendAsync(IEnumerable<OutboxMessage> messages, CancellationToken cancellationToken = default);
        ValueTask<string> LockAsync(OutboxMessage message, CancellationToken cancellationToken = default);
        ValueTask<IEnumerable<OutboxMessage>> ReadPendingAsync(CancellationToken cancellationToken = default);
        ValueTask DeleteAsync(OutboxMessage message, string lockId, CancellationToken cancellationToken = default);
    }
}