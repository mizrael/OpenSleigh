using OpenSleigh.Queries;
using OpenSleigh.Transport;
using OpenSleigh.Utils;
using System.Collections.Concurrent;

namespace OpenSleigh.InMemory;

/// <summary>
/// keeps sagas the way a database does: what is stored is a copy, and every message gets an instance of its own
/// built from it. A handler that fails leaves the stored saga as it was.
/// </summary>
internal class InMemorySagaStateRepository : ISagaStateRepository, ISagaStateQuery, ISagaFailureQuery, ISagaFailurePurger
{
    /// <summary>
    /// how many failure records are kept. When more arrive, the oldest are dropped.
    /// </summary>
    public const int MaxFailures = 1000;

    // replaced as a whole, under the gate, on every lock, release and abandon: readers never see one half changed
    private sealed record StoredSaga(
        string InstanceId,
        string TriggerMessageId,
        string CorrelationId,
        SagaDescriptor Descriptor,
        byte[]? StateData,
        bool IsCompleted,
        ProcessedMessage[] ProcessedMessages,
        string? LockId,
        object Version);

    private static readonly ConcurrentDictionary<Type, IFoundSagaInstanceFactory> _instanceFactories = new();
    private static readonly ConcurrentDictionary<Type, ISagaStateExtractor> _stateExtractors = new();

    private readonly ConcurrentDictionary<string, StoredSaga> _sagasByKey = new();
    private readonly ConcurrentDictionary<string, StoredSaga> _sagasById = new();
    private readonly object _gate = new();

    private readonly Queue<SagaMessageFailure> _failures = new();
    private readonly object _failuresGate = new();

    private readonly ISerializer _serializer;

    public InMemorySagaStateRepository(ISerializer? serializer = null)
    {
        _serializer = serializer ?? new JsonSerializer();
    }

    public ValueTask<ISagaInstance?> FindAsync<TM>(SagaDescriptor descriptor, IMessageContext<TM> messageContext, CancellationToken cancellationToken = default)
        where TM : IMessage
        => ValueTask.FromResult(
            _sagasByKey.TryGetValue(BuildKey(descriptor, messageContext.CorrelationId), out var saga)
                ? CreateInstance(saga)
                : null);

    private ISagaInstance CreateInstance(StoredSaga saga)
    {
        var stateType = saga.Descriptor.SagaStateType;

        var instance = stateType is null
            ? new FoundSagaInstance(saga.InstanceId, saga.TriggerMessageId, saga.CorrelationId, saga.Descriptor, saga.ProcessedMessages, saga.Version)
            : _instanceFactories
                .GetOrAdd(stateType, static type =>
                    (IFoundSagaInstanceFactory)Activator.CreateInstance(typeof(FoundSagaInstanceFactory<>).MakeGenericType(type))!)
                .Create(
                    ReadState(saga.StateData, stateType)!,
                    saga.InstanceId, saga.TriggerMessageId, saga.CorrelationId, saga.Descriptor, saga.ProcessedMessages, saga.Version);

        if (saga.IsCompleted)
            instance.MarkAsCompleted();

        return instance;
    }

    // a saga has no stored state until its first message is done with: until then it is the state a new saga starts with
    private object? ReadState(byte[]? stateData, Type stateType)
        => stateData is null
            ? Activator.CreateInstance(stateType)
            : _serializer.Deserialize(stateData, stateType);

    private byte[]? ExtractStateData(ISagaInstance instance)
    {
        var stateType = instance.Descriptor.SagaStateType;
        if (stateType is null || !instance.GetType().IsGenericType)
            return null;

        return _stateExtractors
            .GetOrAdd(stateType, static type =>
                (ISagaStateExtractor)Activator.CreateInstance(typeof(SagaStateExtractor<>).MakeGenericType(type))!)
            .Extract(instance, _serializer);
    }

    public ValueTask<string> LockAsync(ISagaInstance state, CancellationToken cancellationToken = default)
    {
        var lockId = Guid.NewGuid().ToString();
        var key = BuildKey(state.Descriptor, state.CorrelationId);

        lock (_gate)
        {
            StoredSaga locked;

            if (_sagasById.TryGetValue(state.InstanceId, out var stored))
            {
                if (stored.LockId is not null)
                    throw new LockException($"saga '{state.InstanceId}' is already locked");

                // released by someone else since this instance was built from it: the instance is out of date
                if (state is IFoundSagaInstance found && !ReferenceEquals(found.FoundVersion, stored.Version))
                    throw new OptimisticLockException($"saga '{state.InstanceId}' has changed since it was found");

                locked = stored with { LockId = lockId };
            }
            else
            {
                // someone else started the saga for this correlation in the meantime, under another instance id
                if (_sagasByKey.ContainsKey(key))
                    throw new OptimisticLockException($"saga for correlation '{state.CorrelationId}' was created by someone else");

                locked = new StoredSaga(
                    state.InstanceId,
                    state.TriggerMessageId,
                    state.CorrelationId,
                    state.Descriptor,
                    StateData: null,
                    state.IsCompleted,
                    ProcessedMessages: [],
                    lockId,
                    Version: new object());
            }

            Store(key, locked);
        }

        return ValueTask.FromResult(lockId);
    }

    public ValueTask ReleaseAsync(ISagaInstance state, CancellationToken cancellationToken = default)
    {
        Release(state, SerializeState(state));
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// the state of the instance as it is stored, or null when the saga has none.
    /// It throws when the state cannot be serialized: nothing has been stored by then.
    /// </summary>
    internal byte[]? SerializeState(ISagaInstance state)
        => ExtractStateData(state);

    /// <param name="stateData">what <see cref="SerializeState"/> returned for the instance.</param>
    internal void Release(ISagaInstance state, byte[]? stateData)
    {
        var processedMessages = CopyProcessedMessages(state);

        lock (_gate)
        {
            var stored = GetLockedBy(state);

            Store(BuildKey(state.Descriptor, state.CorrelationId), stored with
            {
                StateData = stateData ?? stored.StateData,
                IsCompleted = state.IsCompleted,
                ProcessedMessages = processedMessages,
                LockId = null,
                Version = new object()
            });
        }

        AddFailure(state.Failure);
    }

    public ValueTask AbandonAsync(ISagaInstance state, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            // the version stays: the saga is as it was, also for whoever found it before it was locked
            var stored = GetLockedBy(state);
            Store(BuildKey(state.Descriptor, state.CorrelationId), stored with { LockId = null });
        }

        AddFailure(state.Failure);

        return ValueTask.CompletedTask;
    }

    private StoredSaga GetLockedBy(ISagaInstance state)
    {
        if (!_sagasById.TryGetValue(state.InstanceId, out var stored))
            throw new ArgumentException($"saga state '{state.InstanceId}' not found");

        if (!string.IsNullOrEmpty(state.LockId) && stored.LockId is not null && stored.LockId != state.LockId)
            throw new LockException($"unable to release saga '{state.InstanceId}' with lock id '{state.LockId}'");

        return stored;
    }

    private void Store(string key, StoredSaga saga)
    {
        _sagasById[saga.InstanceId] = saga;
        _sagasByKey[key] = saga;
    }

    // this runs for every message: the processed messages never change once created, so they are copied as they are
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

    private void AddFailure(SagaMessageFailure? failure)
    {
        if (failure is null)
            return;

        lock (_failuresGate)
        {
            _failures.Enqueue(failure);

            while (_failures.Count > MaxFailures)
                _failures.Dequeue();
        }
    }

    public ValueTask<PagedResult<SagaMessageFailure>> GetAllAsync(SagaFailureQueryFilter? filter = null, CancellationToken cancellationToken = default)
    {
        filter ??= new SagaFailureQueryFilter();

        SagaMessageFailure[] matching;
        lock (_failuresGate)
            matching = _failures.Where(filter.Matches).ToArray();

        var items = matching
            .OrderByDescending(failure => failure.OccurredAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToList();

        return ValueTask.FromResult(new PagedResult<SagaMessageFailure>(items, matching.Length, filter.Page, filter.PageSize));
    }

    public ValueTask<long> PurgeAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        lock (_failuresGate)
        {
            var kept = _failures.Where(failure => failure.OccurredAt >= olderThan).ToArray();
            var removed = _failures.Count - kept.Length;

            _failures.Clear();
            foreach (var failure in kept)
                _failures.Enqueue(failure);

            return ValueTask.FromResult((long)removed);
        }
    }

    public ValueTask<SagaInstanceInfo?> GetByInstanceIdAsync(string instanceId, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(_sagasById.TryGetValue(instanceId, out var saga) ? MapToInfo(saga) : null);

    public ValueTask<SagaInstanceInfo?> GetByCorrelationIdAsync(string correlationId, string sagaType, CancellationToken cancellationToken = default)
    {
        var match = _sagasById.Values.FirstOrDefault(saga =>
            saga.CorrelationId == correlationId && saga.Descriptor.SagaType.FullName == sagaType);

        return ValueTask.FromResult(match is null ? null : MapToInfo(match));
    }

    public ValueTask<PagedResult<SagaInstanceInfo>> GetAllAsync(SagaQueryFilter? filter = null, CancellationToken cancellationToken = default)
    {
        var query = _sagasById.Values.ToList().AsEnumerable();

        if (filter?.SagaType is not null)
            query = query.Where(saga => saga.Descriptor.SagaType.FullName == filter.SagaType);
        if (filter?.IsCompleted is not null)
            query = query.Where(saga => saga.IsCompleted == filter.IsCompleted);

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

    private SagaInstanceInfo MapToInfo(StoredSaga saga)
        => new()
        {
            InstanceId = saga.InstanceId,
            CorrelationId = saga.CorrelationId,
            TriggerMessageId = saga.TriggerMessageId,
            SagaType = saga.Descriptor.SagaType.FullName ?? saga.Descriptor.SagaType.Name,
            SagaStateType = saga.Descriptor.SagaStateType?.FullName,
            IsCompleted = saga.IsCompleted,
            IsLocked = saga.LockId is not null,
            ProcessedMessages = saga.ProcessedMessages
                .Select(pm => new ProcessedMessageInfo(pm.MessageId, pm.When))
                .ToList(),
            StateData = saga.Descriptor.SagaStateType is { } stateType ? ReadState(saga.StateData, stateType) : null
        };

    private static string BuildKey(SagaDescriptor descriptor, string correlationId)
        => $"{correlationId}|{descriptor.SagaType.FullName}|{descriptor.SagaStateType?.FullName ?? string.Empty}";
}
