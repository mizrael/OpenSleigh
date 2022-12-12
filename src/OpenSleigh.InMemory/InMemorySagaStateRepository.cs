using System.Collections.Concurrent;

namespace OpenSleigh.InMemory
{
    internal class InMemorySagaStateRepository : ISagaStateRepository
    {
        private readonly ConcurrentDictionary<string, (ISagaExecutionContext state, string? lockId)> _statesByDescriptor = new();
        private readonly ConcurrentDictionary<string, (ISagaExecutionContext state, string? lockId)> _statesById = new();

        public ValueTask<ISagaExecutionContext?> FindAsync(SagaDescriptor descriptor, string correlationId, CancellationToken cancellationToken = default)
        {
            string key = BuildKey(descriptor, correlationId);

            ISagaExecutionContext? state = null;

            if (_statesByDescriptor.TryGetValue(key, out var val))
                state = val.state;

            return ValueTask.FromResult(state);

        }

        public ValueTask<string> LockAsync(ISagaExecutionContext state, CancellationToken cancellationToken = default)
        {
            string lockId = Guid.NewGuid().ToString();
            
            _statesById.AddOrUpdate(state.InstanceId,
                _ => (state, lockId),
                (k, v) =>
                {
                    if (v.lockId is not null)
                        throw new ApplicationException($"saga '{state.InstanceId}' is already locked");
                    return (state, lockId);
                });

            _statesByDescriptor.AddOrUpdate(state.InstanceId,
               _ => (state, lockId),
               (k, v) =>
               {
                   if (v.lockId is not null)
                       throw new ApplicationException($"saga '{state.InstanceId}' is already locked");
                   return (state, lockId);
               });

            return ValueTask.FromResult(lockId);
        }

        public ValueTask ReleaseAsync(ISagaExecutionContext state, string lockId, CancellationToken cancellationToken = default)
        {
            string key = BuildKey(state.Descriptor, state.CorrelationId);
            _statesByDescriptor.AddOrUpdate(key, _ => (state, lockId), (_, _) => (state, null));

            _statesById.AddOrUpdate(state.InstanceId, _ => (state, lockId), (_, _) => (state, null));

            return ValueTask.CompletedTask;
        }

        private static string BuildKey(SagaDescriptor descriptor, string correlationId)
            => $"{correlationId}|{descriptor.SagaType.FullName}|{(descriptor.SagaStateType is null ? string.Empty : descriptor.SagaStateType.FullName)}";        
    }
}
