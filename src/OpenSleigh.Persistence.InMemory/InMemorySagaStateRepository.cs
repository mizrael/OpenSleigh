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
        private readonly ConcurrentDictionary<string, (SagaState state, Guid? lockId)> _items = new ();
        private static readonly SemaphoreSlim ReleaseSemaphore = new (1, 1);

        public Task<(TD state, Guid lockId)> LockAsync<TD>(Guid correlationId, TD newState = default, CancellationToken cancellationToken = default)
            where TD : SagaState
        {
            var key = BuildKey<TD>(correlationId);
            
            var (state, lockId) = _items.AddOrUpdate(key, 
                k => (newState, Guid.NewGuid()),
                (k, v) =>
                {
                    if(v.lockId.HasValue)
                        throw new LockException($"saga state '{correlationId}' is already locked");
                    return (v.state, Guid.NewGuid());
                });

            return Task.FromResult((state as TD, lockId.Value));
        }

        public Task ReleaseLockAsync<TD>(TD state, Guid lockId, CancellationToken cancellationToken = default)
            where TD : SagaState
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            return ReleaseLockAsyncCore(state, lockId, cancellationToken);
        }

        private async Task ReleaseLockAsyncCore<TD>(TD state, Guid lockId, CancellationToken cancellationToken)
            where TD : SagaState
        {
            await ReleaseSemaphore.WaitAsync(cancellationToken);

            var key = BuildKey<TD>(state.Id);
            
            try
            {
                if (!_items.ContainsKey(key))
                    throw new ArgumentOutOfRangeException(nameof(state), $"invalid state correlationId '{state.Id}'");
                var stored = _items[key];
                if (stored.lockId != lockId)
                    throw new LockException($"unable to release lock on saga state '{state.Id}'");
                _items[key] = (state, null);
            }
            finally
            {
                ReleaseSemaphore.Release();
            }
        }

        private static string BuildKey<TD>(Guid correlationId)
        {
            var stateType = typeof(TD);
            return $"{correlationId}|{stateType.FullName}";
        }
    }
}