using OpenSleigh.Transport;
using OpenSleigh.Outbox;
using System.Collections.Concurrent;

namespace OpenSleigh.InMemory.Outbox;

internal class InMemoryOutboxRepository : IOutboxRepository
{
    private readonly ConcurrentDictionary<string, (MessageEnvelope message, string? lockId)> _messages = new();

    public ValueTask AppendAsync(IEnumerable<MessageEnvelope> messages, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);

        foreach (var message in messages)
            _messages.TryAdd(message.MessageId, (message, null));

        return ValueTask.CompletedTask;
    }

    public ValueTask<IEnumerable<MessageEnvelope>> ReadPendingAsync(CancellationToken cancellationToken = default)
    => ValueTask.FromResult(
        _messages.Values.Where(m => m.lockId == null)
                        .Select(m => m.message));

    public ValueTask<string> LockAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        string lockId = Guid.NewGuid().ToString();
        if (!_messages.TryUpdate(message.MessageId, (message, lockId), (message, null)))
            throw new LockException($"message '{message.MessageId}' is already locked");
        return ValueTask.FromResult(lockId);
    }

    public ValueTask DeleteAsync(MessageEnvelope message, string lockId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (_messages.TryGetValue(message.MessageId, out var tuple) && tuple.lockId == lockId)
            _messages.Remove(message.MessageId, out _);
        return ValueTask.CompletedTask;
    }
}
