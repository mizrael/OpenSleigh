using OpenSleigh.Outbox;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace OpenSleigh.InMemory.Outbox;

internal class InMemoryOutboxRepository : IOutboxRepository
{
    private readonly ConcurrentDictionary<string, MessageEnvelope> _messages = new();

    public ValueTask<OutboxAppendResult> AppendAsync(IEnumerable<MessageEnvelope> messages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        foreach (var message in messages)
            if(!_messages.TryAdd(message.MessageId, message))
                return ValueTask.FromResult(OutboxAppendResult.Duplicate);

        return ValueTask.FromResult(OutboxAppendResult.Success);
    }

    public ValueTask<IEnumerable<MessageEnvelope>> ReadPendingAsync(CancellationToken cancellationToken = default)
    => ValueTask.FromResult((IEnumerable<MessageEnvelope>)_messages.Values);

    public ValueTask DeleteAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _messages.Remove(message.MessageId, out _);

        return ValueTask.CompletedTask;
    }
}
