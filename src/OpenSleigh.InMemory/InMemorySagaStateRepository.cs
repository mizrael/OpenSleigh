using OpenSleigh.Queries;
using OpenSleigh.Transport;
using System.Collections.Concurrent;

namespace OpenSleigh.InMemory;

internal class InMemorySagaStateRepository : ISagaStateRepository, ISagaStateQuery
{
    // processedMessages is what the saga had handled when it was last released. The instance itself keeps changing
    // while its owner handles a message, and its list cannot be read from another thread in the meantime
    private readonly ConcurrentDictionary<string, (ISagaInstance state, string? lockId, IReadOnlyList<ProcessedMessageInfo> processedMessages)> _statesByDescriptor = new();
    private readonly ConcurrentDictionary<string, (ISagaInstance state, string? lockId, IReadOnlyList<ProcessedMessageInfo> processedMessages)> _statesById = new();
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

            // a saga that is locked for the first time has handled nothing yet, as far as anyone else can tell
            IReadOnlyList<ProcessedMessageInfo> released = _statesById.TryGetValue(state.InstanceId, out var known)
                ? known.processedMessages
                : [];

            _statesById[state.InstanceId] = (state, lockId, released);
            _statesByDescriptor[key] = (state, lockId, released);
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

            IReadOnlyList<ProcessedMessageInfo> processedMessages = state.ProcessedMessages
                .Select(pm => new ProcessedMessageInfo(pm.MessageId, pm.When))
                .ToArray();

            _statesByDescriptor[key] = (state, null, processedMessages);
            _statesById[state.InstanceId] = (state, null, processedMessages);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<SagaInstanceInfo?> GetByInstanceIdAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        if (_statesById.TryGetValue(instanceId, out var entry))
            return ValueTask.FromResult<SagaInstanceInfo?>(MapToInfo(entry));
        return ValueTask.FromResult<SagaInstanceInfo?>(null);
    }

    public ValueTask<SagaInstanceInfo?> GetByCorrelationIdAsync(string correlationId, string sagaType, CancellationToken cancellationToken = default)
    {
        var match = _statesById.Values.ToList()
            .FirstOrDefault(e => e.state.CorrelationId == correlationId
                && e.state.Descriptor.SagaType.FullName == sagaType);
        return ValueTask.FromResult(match.state is not null ? MapToInfo(match) : null);
    }

    public ValueTask<PagedResult<SagaInstanceInfo>> GetAllAsync(SagaQueryFilter? filter = null, CancellationToken cancellationToken = default)
    {
        var snapshot = _statesById.Values.ToList();
        var query = snapshot.AsEnumerable();

        if (filter?.SagaType is not null)
            query = query.Where(e => e.state.Descriptor.SagaType.FullName == filter.SagaType);
        if (filter?.IsCompleted is not null)
            query = query.Where(e => e.state.IsCompleted == filter.IsCompleted);

        var totalCount = query.Count();
        var page = filter?.Page ?? 1;
        var pageSize = filter?.PageSize ?? 20;

        var items = query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(MapToInfo)
            .ToList();

        return ValueTask.FromResult(new PagedResult<SagaInstanceInfo>(items, totalCount, page, pageSize));
    }

    private static SagaInstanceInfo MapToInfo((ISagaInstance state, string? lockId, IReadOnlyList<ProcessedMessageInfo> processedMessages) entry)
        => new()
        {
            InstanceId = entry.state.InstanceId,
            CorrelationId = entry.state.CorrelationId,
            TriggerMessageId = entry.state.TriggerMessageId,
            SagaType = entry.state.Descriptor.SagaType.FullName ?? entry.state.Descriptor.SagaType.Name,
            SagaStateType = entry.state.Descriptor.SagaStateType?.FullName,
            IsCompleted = entry.state.IsCompleted,
            IsLocked = entry.lockId is not null,
            ProcessedMessages = entry.processedMessages,
            StateData = ExtractStateData(entry.state)
        };

    private static object? ExtractStateData(ISagaInstance instance)
    {
        var sagaInstanceType = instance.GetType();
        var stateInterface = sagaInstanceType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISagaInstance<>));
        if (stateInterface is null)
            return null;

        var stateProperty = stateInterface.GetProperty("State");
        return stateProperty?.GetValue(instance);
    }

    private static string BuildKey(SagaDescriptor descriptor, string correlationId)
        => $"{correlationId}|{descriptor.SagaType.FullName}|{descriptor.SagaStateType?.FullName ?? string.Empty}";
}
