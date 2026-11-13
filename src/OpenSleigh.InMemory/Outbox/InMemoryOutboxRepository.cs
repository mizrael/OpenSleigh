using OpenSleigh.Outbox;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace OpenSleigh.InMemory.Outbox;

internal class InMemoryOutboxRepository : IOutboxRepository
{
    private readonly ConcurrentDictionary<string, MessageEnvelope> _messages = new();

    // messages that are not due yet are kept apart, ordered by due time:
    // a read only looks at the ones that became due, however many are waiting
    private readonly ConcurrentDictionary<string, MessageEnvelope> _delayedMessages = new();
    private readonly PriorityQueue<MessageEnvelope, DateTimeOffset> _delayedMessagesByDueTime = new();
    private readonly object _delayedMessagesGate = new();

    public ValueTask<OutboxAppendResult> AppendAsync(IEnumerable<MessageEnvelope> messages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        foreach (var message in messages)
            if (!TryAppend(message))
                return ValueTask.FromResult(OutboxAppendResult.Duplicate);

        return ValueTask.FromResult(OutboxAppendResult.Success);
    }

    private bool TryAppend(MessageEnvelope message)
    {
        if (message.DeliverAt is not { } dueTime)
            return !_delayedMessages.ContainsKey(message.MessageId) &&
                   _messages.TryAdd(message.MessageId, message);

        lock (_delayedMessagesGate)
        {
            if (_messages.ContainsKey(message.MessageId) ||
                !_delayedMessages.TryAdd(message.MessageId, message))
                return false;

            _delayedMessagesByDueTime.Enqueue(message, dueTime);
            return true;
        }
    }

    public ValueTask<IEnumerable<MessageEnvelope>> ReadPendingAsync(CancellationToken cancellationToken = default)
    {
        if (!_delayedMessages.IsEmpty)
            MakeDueMessagesPending(DateTimeOffset.UtcNow);

        return ValueTask.FromResult((IEnumerable<MessageEnvelope>)_messages.Values);
    }

    private void MakeDueMessagesPending(DateTimeOffset now)
    {
        lock (_delayedMessagesGate)
        {
            while (_delayedMessagesByDueTime.TryPeek(out var message, out var dueTime) && dueTime <= now)
            {
                _delayedMessagesByDueTime.Dequeue();
                _messages.TryAdd(message.MessageId, message);
                _delayedMessages.TryRemove(message.MessageId, out _);
            }
        }
    }

    public ValueTask DeleteAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _messages.Remove(message.MessageId, out _);

        return ValueTask.CompletedTask;
    }
}
