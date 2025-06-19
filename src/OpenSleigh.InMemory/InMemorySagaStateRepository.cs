using OpenSleigh.Transport;
using System.Collections.Concurrent;

namespace OpenSleigh.InMemory;

internal class InMemorySagaStateRepository : ISagaStateRepository
{
    private readonly ConcurrentDictionary<string, (ISagaInstance  state, string? lockId)> _statesByDescriptor = new();
    private readonly ConcurrentDictionary<string, (ISagaInstance  state, string? lockId)> _statesById = new();

    public ValueTask<ISagaInstance ?> FindAsync<TM>(SagaDescriptor descriptor, IMessageContext<TM> messageContext, CancellationToken cancellationToken = default)
        where TM : IMessage
    {
        string key = BuildKey(descriptor, messageContext.CorrelationId);

        ISagaInstance ? state = null;

        if (_statesByDescriptor.TryGetValue(key, out var val))
            state = val.state;

        return ValueTask.FromResult(state);

    }

    public ValueTask<string> LockAsync(ISagaInstance  state, CancellationToken cancellationToken = default)
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

        string key = BuildKey(state.Descriptor, state.CorrelationId);
        _statesByDescriptor.AddOrUpdate(key,
           _ => (state, lockId),
           (k, v) =>
           {
               if (v.lockId is not null)
                   throw new ApplicationException($"saga '{state.InstanceId}' is already locked");
               return (state, lockId);
           });

        return ValueTask.FromResult(lockId);
    }

    public ValueTask ReleaseAsync(ISagaInstance  state, CancellationToken cancellationToken = default)
    {
        string key = BuildKey(state.Descriptor, state.CorrelationId);
        _statesByDescriptor.AddOrUpdate(key, _ => (state, null), (_, _) => (state, null));

        _statesById.AddOrUpdate(state.InstanceId, _ => (state, null), (_, _) => (state, null));

        return ValueTask.CompletedTask;
    }

    private static string BuildKey(SagaDescriptor descriptor, string correlationId) 
        => $"{correlationId}|{descriptor.SagaType.FullName}|{descriptor.SagaStateType?.FullName ?? string.Empty}";        
}
