using OpenSleigh.Transport;
using System.Collections.Concurrent;

namespace OpenSleigh.InMemory;

internal class InMemorySagaStateRepository : ISagaStateRepository
{
    private readonly ConcurrentDictionary<string, (ISagaInstance state, string? lockId)> _statesByDescriptor = new();
    private readonly ConcurrentDictionary<string, (ISagaInstance state, string? lockId)> _statesById = new();
    private readonly object _lockSync = new();

    public ValueTask<ISagaInstance?> FindAsync<TM>(SagaDescriptor descriptor, IMessageContext<TM> messageContext, CancellationToken cancellationToken = default)
        where TM : IMessage
    {
        string key = BuildKey(descriptor, messageContext.CorrelationId);

        ISagaInstance? state = null;

        if (_statesByDescriptor.TryGetValue(key, out var val))
            state = val.state;

        return ValueTask.FromResult(state);
    }

    public ValueTask<string> LockAsync(ISagaInstance state, CancellationToken cancellationToken = default)
    {
        string lockId = Guid.NewGuid().ToString();
        string key = BuildKey(state.Descriptor, state.CorrelationId);

        lock (_lockSync)
        {
            if (_statesById.TryGetValue(state.InstanceId, out var byId) && byId.lockId is not null)
                throw new LockException($"saga '{state.InstanceId}' is already locked");

            if (_statesByDescriptor.TryGetValue(key, out var byDesc) && byDesc.lockId is not null)
                throw new OptimisticLockException($"saga '{state.InstanceId}' is already locked");

            _statesById[state.InstanceId] = (state, lockId);
            _statesByDescriptor[key] = (state, lockId);
        }

        return ValueTask.FromResult(lockId);
    }

    public ValueTask ReleaseAsync(ISagaInstance state, CancellationToken cancellationToken = default)
    {
        string key = BuildKey(state.Descriptor, state.CorrelationId);

        lock (_lockSync)
        {
            if (!string.IsNullOrEmpty(state.LockId)
                && _statesById.TryGetValue(state.InstanceId, out var entry)
                && entry.lockId is not null
                && entry.lockId != state.LockId)
                throw new LockException($"unable to release saga '{state.InstanceId}' with lock id '{state.LockId}'");

            _statesByDescriptor[key] = (state, null);
            _statesById[state.InstanceId] = (state, null);
        }

        return ValueTask.CompletedTask;
    }

    private static string BuildKey(SagaDescriptor descriptor, string correlationId)
        => $"{correlationId}|{descriptor.SagaType.FullName}|{descriptor.SagaStateType?.FullName ?? string.Empty}";
}
