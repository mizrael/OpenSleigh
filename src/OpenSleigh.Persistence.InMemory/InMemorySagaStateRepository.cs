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
        private static SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public InMemorySagaStateRepository()
        {
            _items = new ConcurrentDictionary<Guid, (SagaState state, Guid? lockId)>();
        }

        public async Task<(TD state, Guid lockId)> LockAsync<TD>(Guid correlationId, TD newState = default, CancellationToken cancellationToken = default)
            where TD : SagaState
        {
            var (state, lockId) = _items.AddOrUpdate(correlationId, k => (newState, Guid.NewGuid()), (k, v) => throw new LockException($"saga state '{correlationId}' is already locked"));

            return (state as TD, lockId.Value);
        }

        public async Task UpdateAsync<TD>(TD state, Guid lockId, bool releaseLock = false, CancellationToken cancellationToken = default)
            where TD : SagaState
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            await _semaphore.WaitAsync(cancellationToken);

            try
            {
                if (!_items.ContainsKey(state.Id))
                    throw new ArgumentOutOfRangeException(nameof(SagaState.Id), $"invalid state correlationId '{state.Id}'");
                var stored = _items[state.Id];
                if (stored.lockId != lockId)
                    throw new LockException($"unable to release lock on saga state '{state.Id}'");
                _items[state.Id] = (state, releaseLock ? null : stored.lockId);
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}