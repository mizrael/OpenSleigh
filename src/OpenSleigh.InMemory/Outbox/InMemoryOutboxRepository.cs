using OpenSleigh.Transport;
using OpenSleigh.Outbox;
using System.Collections.Concurrent;

namespace OpenSleigh.InMemory.Outbox
{
    internal class InMemoryOutboxRepository : IOutboxRepository
    {
        private readonly ConcurrentDictionary<string, (OutboxMessage message, string? lockId)> _messages = new();

        public ValueTask AppendAsync(IEnumerable<OutboxMessage> messages, CancellationToken cancellationToken)
        {
            if (messages == null)
                throw new ArgumentNullException(nameof(messages));

            foreach (var message in messages)
                _messages.TryAdd(message.MessageId, (message, null));

            return ValueTask.CompletedTask;
        }

        public ValueTask<IEnumerable<OutboxMessage>> ReadPendingAsync(CancellationToken cancellationToken)
        => ValueTask.FromResult(
            _messages.Values.Where(m => m.lockId == null)
                            .Select(m => m.message));

        public ValueTask<string> LockAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            string lockId = Guid.NewGuid().ToString();
            if (!_messages.TryUpdate(message.MessageId, (message, lockId), (message, null)))
                throw new LockException($"message '{message.MessageId}' is already locked");
            return ValueTask.FromResult(lockId);
        }

        public ValueTask DeleteAsync(OutboxMessage message, string lockId, CancellationToken cancellationToken)
        {
            if (_messages.ContainsKey(message.MessageId) && _messages[message.MessageId].lockId == lockId)
                _messages.Remove(message.MessageId, out _);
            return ValueTask.CompletedTask;
        }
    }
}
