using OpenSleigh.Core;
using OpenSleigh.Core.Exceptions;
using OpenSleigh.Core.Persistence;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Persistence.InMemory
{
    public class InMemorySagaStateRepository : ISagaStateRepository
    {
        private readonly ConcurrentDictionary<Guid, (SagaState state, Guid? lockId)> _items;
        private static readonly SemaphoreSlim _semaphore = new (1, 1);

        public InMemorySagaStateRepository()
        {
            _items = new ConcurrentDictionary<Guid, (SagaState state, Guid? lockId)>();
        }

        public async Task<(TD state, Guid lockId)> LockAsync<TD>(Guid correlationId, TD newState = default, CancellationToken cancellationToken = default)
            where TD : SagaState
        {
            var (state, lockId) = _items.AddOrUpdate(correlationId, 
                k => (newState, Guid.NewGuid()),
                (k, v) =>
                {
                    if(v.lockId.HasValue)
                        throw new LockException($"saga state '{correlationId}' is already locked");
                    return (v.state, Guid.NewGuid());
                });

            return (state as TD, lockId.Value);
        }

        public async Task ReleaseLockAsync<TD>(TD state, Guid lockId, ITransaction transaction = null, CancellationToken cancellationToken = default)
            where TD : SagaState
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            await _semaphore.WaitAsync(cancellationToken);

            try
            {
                if (!_items.ContainsKey(state.Id))
                    throw new ArgumentOutOfRangeException(nameof(SagaState), $"invalid state correlationId '{state.Id}'");
                var stored = _items[state.Id];
                if (stored.lockId != lockId)
                    throw new LockException($"unable to release lock on saga state '{state.Id}'");
                _items[state.Id] = (state, null);
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}