namespace OpenSleigh.Outbox;

public interface IOutboxRepository
{
    ValueTask AppendAsync(IEnumerable<MessageEnvelope> messages, CancellationToken cancellationToken = default);
    ValueTask<string> LockAsync(MessageEnvelope message, CancellationToken cancellationToken = default);
    ValueTask<IEnumerable<MessageEnvelope>> ReadPendingAsync(CancellationToken cancellationToken = default);
    ValueTask DeleteAsync(MessageEnvelope message, string lockId, CancellationToken cancellationToken = default);
}