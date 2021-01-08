using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Persistence;

namespace OpenSleigh.Persistence.InMemory.Messaging
{
    public class InMemoryOutboxRepository : IOutboxRepository
    {
        private readonly ConcurrentDictionary<Guid, IMessage> _messages = new();
        
        public Task<IEnumerable<IMessage>> ReadMessagesToProcess(CancellationToken cancellationToken = default) =>
            Task.FromResult((IEnumerable<IMessage>)_messages.Values);

        public Task MarkAsSentAsync(IMessage message, CancellationToken cancellationToken = default)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));
            _messages.TryRemove(message.Id, out _);
            return Task.CompletedTask;
        }

        public Task AppendAsync(IMessage message, ITransaction transaction = null, CancellationToken cancellationToken = default)
        {
            if (message == null) 
                throw new ArgumentNullException(nameof(message));
            _messages.TryAdd(message.Id, message);
            return Task.CompletedTask;
        }

        public Task CleanProcessedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
