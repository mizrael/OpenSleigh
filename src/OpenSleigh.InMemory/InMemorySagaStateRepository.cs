using OpenSleigh.Queries;
using OpenSleigh.Transport;
using OpenSleigh.Utils;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace OpenSleigh.InMemory;

internal class InMemorySagaStateRepository : ISagaStateRepository, ISagaStateQuery
{
    /// <summary>
    /// what a saga was when it was last released. The instance itself keeps changing while its owner handles a message:
    /// its list cannot be read from another thread in the meantime, and this is what the saga goes back to when abandoned.
    /// </summary>
    /// <param name="StateData">null when the saga has no state, or one that cannot be serialized.</param>
    private sealed record Snapshot(ProcessedMessage[] ProcessedMessages, bool IsCompleted, byte[]? StateData);

    /// <param name="Released">null for a saga that was locked and never released.</param>
    private readonly record struct Entry(ISagaInstance State, string? LockId, Snapshot? Released);

    private readonly ConcurrentDictionary<string, Entry> _statesByDescriptor = new();
    private readonly ConcurrentDictionary<string, Entry> _statesById = new();
    private readonly object _lockSync = new();

    // instances that were abandoned: whoever found one before that has to look for the saga again
    private readonly ConditionalWeakTable<ISagaInstance, object> _abandoned = new();
    private static readonly object Abandoned = new();

    private static readonly ConcurrentDictionary<Type, ISagaStateAccessor> StateAccessors = new();
    private static readonly ConcurrentDictionary<Type, byte> StatesThatCannotBeSerialized = new();

    private readonly ISerializer _serializer;

    public InMemorySagaStateRepository(ISerializer? serializer = null)
    {
        _serializer = serializer ?? new JsonSerializer();
    }

    public ValueTask<ISagaInstance?> FindAsync<TM>(SagaDescriptor descriptor, IMessageContext<TM> messageContext, CancellationToken cancellationToken = default)
        where TM : IMessage
    {
        string key = BuildKey(descriptor, messageContext.CorrelationId);

        ISagaInstance? state = null;

        if (_statesByDescriptor.TryGetValue(key, out var val))
            state = val.State;

        return ValueTask.FromResult(state);
    }

    public ValueTask<string> LockAsync(ISagaInstance state, CancellationToken cancellationToken = default)
    {
        string lockId = Guid.NewGuid().ToString();
        string key = BuildKey(state.Descriptor, state.CorrelationId);

        lock (_lockSync)
        {
            if (_abandoned.TryGetValue(state, out _))
                throw new OptimisticLockException($"saga '{state.InstanceId}' has changed since it was found");

            if (_statesById.TryGetValue(state.InstanceId, out var byId) && byId.LockId is not null)
                throw new LockException($"saga '{state.InstanceId}' is already locked");

            if (_statesByDescriptor.TryGetValue(key, out var byDesc) && byDesc.LockId is not null)
                throw new OptimisticLockException($"saga '{state.InstanceId}' is already locked");

            Store(key, new Entry(state, lockId, byId.Released));
        }

        return ValueTask.FromResult(lockId);
    }

    public ValueTask ReleaseAsync(ISagaInstance state, CancellationToken cancellationToken = default)
    {
        string key = BuildKey(state.Descriptor, state.CorrelationId);

        // outside the lock: only the owner of the saga changes its state
        var stateData = SerializeState(state);

        lock (_lockSync)
        {
            if (!string.IsNullOrEmpty(state.LockId)
                && _statesById.TryGetValue(state.InstanceId, out var entry)
                && entry.LockId is not null
                && entry.LockId != state.LockId)
                throw new LockException($"unable to release saga '{state.InstanceId}' with lock id '{state.LockId}'");

            Store(key, new Entry(state, null, new Snapshot(CopyProcessedMessages(state), state.IsCompleted, stateData)));
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// releases the saga as it was when it was last released: what its handler did to it since is dropped.
    /// A saga that was never released is removed: the message that starts it again gets a new instance.
    /// </summary>
    /// <remarks>
    /// A state that cannot be serialized cannot be put back: such a saga keeps what the handler did to its state.
    /// </remarks>
    public ValueTask AbandonAsync(ISagaInstance state, CancellationToken cancellationToken = default)
    {
        string key = BuildKey(state.Descriptor, state.CorrelationId);

        lock (_lockSync)
        {
            if (!_statesById.TryGetValue(state.InstanceId, out var entry))
                return ValueTask.CompletedTask;

            if (!string.IsNullOrEmpty(state.LockId) && entry.LockId is not null && entry.LockId != state.LockId)
                throw new LockException($"unable to release saga '{state.InstanceId}' with lock id '{state.LockId}'");

            _abandoned.AddOrUpdate(entry.State, Abandoned);

            if (entry.Released is null)
            {
                _statesById.TryRemove(state.InstanceId, out _);
                _statesByDescriptor.TryRemove(key, out _);
            }
            else
            {
                Store(key, new Entry(Restore(entry.State, entry.Released), null, entry.Released));
            }
        }

        return ValueTask.CompletedTask;
    }

    private void Store(string key, Entry entry)
    {
        _statesById[entry.State.InstanceId] = entry;
        _statesByDescriptor[key] = entry;
    }

    // this runs for every message of a saga that has a state
    private byte[]? SerializeState(ISagaInstance instance)
    {
        var stateType = instance.Descriptor.SagaStateType;
        if (stateType is null || StatesThatCannotBeSerialized.ContainsKey(stateType))
            return null;

        var state = StateAccessorFor(stateType).GetState(instance);
        if (state is null)
            return null;

        try
        {
            return _serializer.Serialize(state);
        }
        catch (Exception e) when (e is NotSupportedException or InvalidOperationException or System.Text.Json.JsonException)
        {
            // remembered: failing for every message would cost more than its handler does
            StatesThatCannotBeSerialized.TryAdd(stateType, 0);
            return null;
        }
    }

    // a new instance: the one the handler worked on cannot be told to forget what it did
    private ISagaInstance Restore(ISagaInstance abandoned, Snapshot released)
    {
        var descriptor = abandoned.Descriptor;
        var stateType = descriptor.SagaStateType;

        ISagaInstance restored;
        if (stateType is null)
        {
            restored = new SagaInstance(
                abandoned.InstanceId, abandoned.TriggerMessageId, abandoned.CorrelationId, descriptor, released.ProcessedMessages);
        }
        else
        {
            var accessor = StateAccessorFor(stateType);
            var state = released.StateData is null
                ? accessor.GetState(abandoned)
                : _serializer.Deserialize(released.StateData, stateType);
            restored = accessor.CreateInstance(abandoned, state, released.ProcessedMessages);
        }

        if (released.IsCompleted)
            restored.MarkAsCompleted();

        return restored;
    }

    private static ISagaStateAccessor StateAccessorFor(Type stateType)
        => StateAccessors.GetOrAdd(stateType, static type =>
            (ISagaStateAccessor)Activator.CreateInstance(typeof(SagaStateAccessor<>).MakeGenericType(type))!);

    // this runs for every message: the processed messages never change once created, so they are copied as they are,
    // into one array, and only turned into what a query returns when a query asks
    private static ProcessedMessage[] CopyProcessedMessages(ISagaInstance state)
    {
        var source = state.ProcessedMessages;
        if (source.Count == 0)
            return [];

        var copy = new ProcessedMessage[source.Count];
        if (source is ICollection<ProcessedMessage> collection)
        {
            collection.CopyTo(copy, 0);
        }
        else
        {
            var index = 0;
            foreach (var message in source)
                copy[index++] = message;
        }

        return copy;
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
            .FirstOrDefault(e => e.State.CorrelationId == correlationId
                && e.State.Descriptor.SagaType.FullName == sagaType);
        return ValueTask.FromResult(match.State is not null ? MapToInfo(match) : null);
    }

    public ValueTask<PagedResult<SagaInstanceInfo>> GetAllAsync(SagaQueryFilter? filter = null, CancellationToken cancellationToken = default)
    {
        var snapshot = _statesById.Values.ToList();
        var query = snapshot.AsEnumerable();

        if (filter?.SagaType is not null)
            query = query.Where(e => e.State.Descriptor.SagaType.FullName == filter.SagaType);
        if (filter?.IsCompleted is not null)
            query = query.Where(e => e.State.IsCompleted == filter.IsCompleted);

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

    private static SagaInstanceInfo MapToInfo(Entry entry)
        => new()
        {
            InstanceId = entry.State.InstanceId,
            CorrelationId = entry.State.CorrelationId,
            TriggerMessageId = entry.State.TriggerMessageId,
            SagaType = entry.State.Descriptor.SagaType.FullName ?? entry.State.Descriptor.SagaType.Name,
            SagaStateType = entry.State.Descriptor.SagaStateType?.FullName,
            IsCompleted = entry.State.IsCompleted,
            IsLocked = entry.LockId is not null,
            ProcessedMessages = (entry.Released?.ProcessedMessages ?? [])
                .Select(pm => new ProcessedMessageInfo(pm.MessageId, pm.When))
                .ToList(),
            StateData = ExtractStateData(entry.State)
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
