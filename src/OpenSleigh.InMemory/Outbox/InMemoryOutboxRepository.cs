using OpenSleigh.Outbox;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace OpenSleigh.InMemory.Outbox;

internal class InMemoryOutboxRepository : IOutboxRepository, IOutboxReturnRepository
{
    private readonly ConcurrentDictionary<string, MessageEnvelope> _messages = new();

    // messages that are not due yet are kept apart, ordered by due time:
    // a read only looks at the ones that became due, however many are waiting
    private readonly Dictionary<string, MessageEnvelope> _delayedMessages = new();
    private readonly PriorityQueue<MessageEnvelope, DateTimeOffset> _delayedMessagesByDueTime = new();

    // read without the gate, by the reads and deletes that have nothing to do when no message is delayed
    private volatile int _delayedMessagesCount;

    // guards the delayed messages, and every append: a message id is accepted once, delayed or not
    private readonly object _gate = new();

    private readonly TimeProvider _timeProvider;

    public InMemoryOutboxRepository() : this(TimeProvider.System)
    {
    }

    internal InMemoryOutboxRepository(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public ValueTask<OutboxAppendResult> AppendAsync(IEnumerable<MessageEnvelope> messages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        lock (_gate)
        {
            foreach (var message in messages)
                if (!TryAppend(message))
                    return ValueTask.FromResult(OutboxAppendResult.Duplicate);
        }

        return ValueTask.FromResult(OutboxAppendResult.Success);
    }

    private bool TryAppend(MessageEnvelope message)
    {
        if (_delayedMessages.ContainsKey(message.MessageId))
            return false;

        if (message.DeliverAt is not { } dueTime)
            return _messages.TryAdd(message.MessageId, message);

        if (_messages.ContainsKey(message.MessageId))
            return false;

        _delayedMessages.Add(message.MessageId, message);
        _delayedMessagesByDueTime.Enqueue(message, dueTime);
        _delayedMessagesCount = _delayedMessages.Count;
        return true;
    }

    public ValueTask<IEnumerable<MessageEnvelope>> ReadPendingAsync(CancellationToken cancellationToken = default)
    {
        if (_delayedMessagesCount > 0)
            MakeDueMessagesPending(_timeProvider.GetUtcNow());

        return ValueTask.FromResult((IEnumerable<MessageEnvelope>)_messages.Values);
    }

    private void MakeDueMessagesPending(DateTimeOffset now)
    {
        lock (_gate)
        {
            while (_delayedMessagesByDueTime.TryPeek(out var message, out var dueTime) && dueTime <= now)
            {
                _delayedMessagesByDueTime.Dequeue();

                // a deleted message stays in the queue, which cannot remove it, and its id may be waiting again
                // with another due time: only the message this entry was queued for is released
                if (!_delayedMessages.TryGetValue(message.MessageId, out var waiting) || !ReferenceEquals(waiting, message))
                    continue;

                // pending before it stops being delayed: a delete always finds it in one of the two
                _messages.TryAdd(message.MessageId, message);
                _delayedMessages.Remove(message.MessageId);
            }

            _delayedMessagesCount = _delayedMessages.Count;
        }
    }

    public ValueTask DeleteAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        // read before looking at the pending messages: one that becomes pending in between was counted as delayed
        var hasDelayedMessages = _delayedMessagesCount > 0;

        if (!TryDeletePending(message) && hasDelayedMessages)
            DeleteDelayed(message);

        return ValueTask.CompletedTask;
    }

    // a message the transport returned is put back, possibly before the relay that published it deletes it:
    // only the message the relay read is deleted, not another one with its id
    private bool TryDeletePending(MessageEnvelope message)
        => _messages.TryRemove(new KeyValuePair<string, MessageEnvelope>(message.MessageId, message));

    private void DeleteDelayed(MessageEnvelope message)
    {
        lock (_gate)
        {
            // it may have become pending while this delete waited for the gate
            if (_delayedMessages.TryGetValue(message.MessageId, out var delayed))
            {
                if (ReferenceEquals(delayed, message))
                    _delayedMessages.Remove(message.MessageId);

                _delayedMessagesCount = _delayedMessages.Count;
            }
            else
            {
                TryDeletePending(message);
            }
        }
    }

    public ValueTask PutBackAsync(IReadOnlyCollection<MessageEnvelope> messages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        lock (_gate)
        {
            foreach (var message in messages)
            {
                // the time of first return that is kept is the earliest, whoever stores it
                var stored = _delayedMessages.GetValueOrDefault(message.MessageId) ?? _messages.GetValueOrDefault(message.MessageId);
                var putBack = stored?.FirstReturnedAt is { } storedFirstReturn && message.DeliverAt is { } dueTime
                    ? message.Returned(storedFirstReturn, dueTime)
                    : message;

                _messages.TryRemove(message.MessageId, out _);
                _delayedMessages.Remove(message.MessageId);
                TryAppend(putBack);
            }

            _delayedMessagesCount = _delayedMessages.Count;
        }

        return ValueTask.CompletedTask;
    }
}
