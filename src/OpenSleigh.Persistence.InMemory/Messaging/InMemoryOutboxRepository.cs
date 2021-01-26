using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core.Exceptions;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Persistence;

namespace OpenSleigh.Persistence.InMemory.Messaging
{
    public class InMemoryOutboxRepository : IOutboxRepository
    {
        private readonly ConcurrentDictionary<Guid, (IMessage message, Guid? lockId, bool processed)> _messages = new();
        
        public Task<IEnumerable<IMessage>> ReadMessagesToProcess(CancellationToken cancellationToken = default) =>
            Task.FromResult(_messages.Values.Where(m => m.lockId == null && !m.processed)
                .Select(m => m.message));

        public Task ReleaseAsync(IMessage message, Guid lockId, CancellationToken cancellationToken = default)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            if(!_messages.TryUpdate(message.Id, (message, null, true), (message, lockId, false)))
                throw new ArgumentException($"message '{message.Id}' not found. Maybe it was already processed?");
            
            return Task.CompletedTask;
        }

        public Task AppendAsync(IMessage message, CancellationToken cancellationToken = default)
        {
            if (message == null) 
                throw new ArgumentNullException(nameof(message));
            _messages.TryAdd(message.Id, (message, null, false));
            return Task.CompletedTask;
        }

        public Task CleanProcessedAsync(CancellationToken cancellationToken = default)
        {
            var processedMessages = _messages.Values.Where(m => m.processed);
            foreach (var msg in processedMessages)
                _messages.TryRemove(msg.message.Id, out var _);

            return Task.CompletedTask;
        }

        public Task<Guid> LockAsync(IMessage message, CancellationToken cancellationToken = default)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));
            
            var lockId = Guid.NewGuid();
            if(!_messages.TryUpdate(message.Id, (message, lockId, false), (message, null, false)))
                throw new LockException($"message '{message.Id}' is already locked");
            return Task.FromResult(lockId);
        }
    }
}
