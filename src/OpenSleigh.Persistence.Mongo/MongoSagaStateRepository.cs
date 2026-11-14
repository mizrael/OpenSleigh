using MongoDB.Bson;
using MongoDB.Driver;
using OpenSleigh.Transport;
using OpenSleigh.Utils;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace OpenSleigh.Persistence.Mongo;

[ExcludeFromCodeCoverage]
public record MongoSagaStateRepositoryOptions(TimeSpan LockMaxDuration)
{
    public static readonly MongoSagaStateRepositoryOptions Default = new MongoSagaStateRepositoryOptions(TimeSpan.FromMinutes(1));
}

public partial class MongoSagaStateRepository : ISagaStateRepository
{
    private readonly IDbContext _dbContext;
    private readonly MongoSagaStateRepositoryOptions _options;
    private readonly ISerializer _serializer;

    private static readonly ConcurrentDictionary<Type, ISagaContextFactory> _contextFactories = new();
    private static readonly ConcurrentDictionary<Type, ISagaStateExtractor> _stateExtractors = new();

    // the lock id each saga had when FindAsync returned it. Keyed by the instance itself:
    // another message gets another instance, and nothing outlives its instance
    private readonly ConditionalWeakTable<ISagaInstance, FoundSaga> _foundSagas = new();

    // the saga this repository last looked for and did not find: locking it stores it, without looking again.
    // One reference, replaced as a whole: a repository shared by several messages can lose it, never corrupt it
    private string? _sagaNotFound;

    private sealed record FoundSaga(string? LockId);

    public MongoSagaStateRepository(
        IDbContext dbContext,
        MongoSagaStateRepositoryOptions? options,
        ISerializer serializer)
    {
        _dbContext = dbContext;
        _options = options ?? MongoSagaStateRepositoryOptions.Default;
        _serializer = serializer;
    }

    public async ValueTask<ISagaInstance?> FindAsync<TM>(SagaDescriptor descriptor, IMessageContext<TM> messageContext, CancellationToken cancellationToken = default)
        where TM : IMessage
    {
        var correlationId = messageContext.CorrelationId;

        var filterBuilder = Builders<Entities.SagaState>.Filter;

        var stateTypeFilter =
            descriptor.SagaStateType == null ?
            filterBuilder.Eq(e => e.SagaStateType, null) :
            filterBuilder.Eq(e => e.SagaStateType, descriptor.SagaStateType.FullName);

        var filter = filterBuilder.And(
                filterBuilder.Eq(e => e.CorrelationId, correlationId),
                filterBuilder.Eq(e => e.SagaType, descriptor.SagaType.FullName),
                stateTypeFilter);

        _sagaNotFound = null;

        var entity = await _dbContext.SagaStates.FindOneAsync(filter, cancellationToken)
                                     .ConfigureAwait(false);

        if (entity is null)
        {
            _sagaNotFound = SagaKey(correlationId, descriptor);
            return null;
        }

        ISagaInstance? result;

        if (descriptor.SagaStateType is null)
            result = new SagaInstance(
                instanceId: entity.InstanceId,
                triggerMessageId: entity.TriggerMessageId,
                correlationId: entity.CorrelationId,
                descriptor: descriptor,
                processedMessages: entity.ProcessedMessages.Select(e => new ProcessedMessage()
                {
                    MessageId = e.MessageId,
                    When = e.When
                }));
        else
        {
            var state = ReadState(entity.StateData, descriptor.SagaStateType);
            var factory = _contextFactories.GetOrAdd(descriptor.SagaStateType!, static t =>
                (ISagaContextFactory)Activator.CreateInstance(
                    typeof(SagaContextFactory<>).MakeGenericType(t))!);
            result = factory.Create(state!, entity, descriptor);
        }

        if (entity.IsCompleted)
            result.MarkAsCompleted();

        _foundSagas.AddOrUpdate(result, new FoundSaga(entity.LockId));

        return result;
    }

    // a saga stored by an older version has no state until its first message is done with:
    // whoever finds it in the meantime sees a new state, as that message did
    private object? ReadState(byte[]? stateData, Type stateType)
        => stateData is null
            ? Activator.CreateInstance(stateType)
            : _serializer.Deserialize(stateData, stateType);

    private byte[]? ExtractStateData(ISagaInstance state)
    {
        if (state.Descriptor.SagaStateType is null || !state.GetType().IsGenericType)
            return null;

        var extractor = _stateExtractors.GetOrAdd(state.Descriptor.SagaStateType, static t =>
            (ISagaStateExtractor)Activator.CreateInstance(
                typeof(SagaStateExtractor<>).MakeGenericType(t))!);
        return extractor.Extract(state, _serializer);
    }

    public ValueTask<string> LockAsync(ISagaInstance state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        return LockAsyncCore(state, cancellationToken);
    }

    private async ValueTask<string> LockAsyncCore(ISagaInstance state, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var lockId = Guid.NewGuid().ToString();

        if (_foundSagas.TryGetValue(state, out var found))
        {
            await LockFoundSagaAsync(state, found, lockId, cancellationToken).ConfigureAwait(false);
            return lockId;
        }

        // a saga this repository looked for and did not find is new: it is stored locked, in one write
        if (WasNotFound(state))
        {
            await InsertLockedAsync(state, lockId).ConfigureAwait(false);
            return lockId;
        }

        var written = await WriteLockAsync(Unlocked(state.InstanceId), lockId).ConfigureAwait(false);
        if (written.IsAcknowledged && written.MatchedCount > 0)
            return lockId;

        var entity = await _dbContext.SagaStates.FindOneAsync(WithInstanceId(state.InstanceId), cancellationToken)
                                                .ConfigureAwait(false);
        if (entity is null)
        {
            await InsertLockedAsync(state, lockId).ConfigureAwait(false);
            return lockId;
        }

        // the saga exists, and the server was not asked whether the lock was written: it is taken as written
        if (!written.IsAcknowledged)
            return lockId;

        if (!await TryTakeOverExpiredLockAsync(entity, lockId).ConfigureAwait(false))
            throw AlreadyLocked(state);

        return lockId;
    }

    /// <summary>
    /// locks a saga that <see cref="FindAsync"/> returned, with one update that only applies while the saga
    /// is still as it was found: every lock and every release leaves another lock id behind.
    /// </summary>
    /// <exception cref="LockException">someone else holds the lock.</exception>
    /// <exception cref="OptimisticLockException">the saga was locked and released by someone else since it was found:
    /// what was found is out of date, and the saga has to be looked for again.</exception>
    private async ValueTask LockFoundSagaAsync(ISagaInstance state, FoundSaga found, string lockId, CancellationToken cancellationToken)
    {
        var filterBuilder = Builders<Entities.SagaState>.Filter;
        var unlockedAsFound = filterBuilder.And(
            Unlocked(state.InstanceId),
            filterBuilder.Eq(e => e.LockId, found.LockId));

        if (await TryLockAsync(unlockedAsFound, lockId).ConfigureAwait(false))
        {
            _foundSagas.Remove(state);
            return;
        }

        // the saga is locked, or no longer as it was found: it is read, to tell which
        var entity = await _dbContext.SagaStates.FindOneAsync(WithInstanceId(state.InstanceId), cancellationToken)
                                                .ConfigureAwait(false);

        if (entity is not null && IsLocked(entity))
            throw AlreadyLocked(state);

        if (entity is null || entity.LockId != found.LockId)
        {
            _foundSagas.Remove(state);
            throw new OptimisticLockException($"saga state '{state.InstanceId}' has changed since it was found");
        }

        // still the lock that was found: its owner never released the saga, which is as it was found
        if (!await TryTakeOverExpiredLockAsync(entity, lockId).ConfigureAwait(false))
            throw AlreadyLocked(state);

        _foundSagas.Remove(state);
    }

    // a lock can have expired without the server being able to tell: a lock time written by an older version
    // is stored in another format. It is taken over only if it is still the one that was just read
    private async ValueTask<bool> TryTakeOverExpiredLockAsync(Entities.SagaState entity, string lockId)
    {
        if (entity.LockId is null || IsLocked(entity))
            return false;

        var filterBuilder = Builders<Entities.SagaState>.Filter;
        var stillTheSameLock = filterBuilder.And(
            WithInstanceId(entity.InstanceId),
            filterBuilder.Eq(e => e.LockId, entity.LockId));

        return await TryLockAsync(stillTheSameLock, lockId).ConfigureAwait(false);
    }

    private bool IsLocked(Entities.SagaState entity)
        => entity.LockId is not null &&
           entity.LockTime is not null &&
           entity.LockTime > DateTimeOffset.UtcNow - _options.LockMaxDuration;

    // a saga is unlocked when it has no lock time, or one that has expired. A released saga keeps a lock id
    private FilterDefinition<Entities.SagaState> Unlocked(string instanceId)
    {
        var filterBuilder = Builders<Entities.SagaState>.Filter;
        var lockExpiredBefore = DateTimeOffset.UtcNow - _options.LockMaxDuration;
        return filterBuilder.And(
            WithInstanceId(instanceId),
            filterBuilder.Or(
                filterBuilder.Eq(e => e.LockId, null),
                filterBuilder.Eq(e => e.LockTime, null),
                filterBuilder.Lte(e => e.LockTime, lockExpiredBefore)));
    }

    // one conditional update: the lock is taken only if the filter still holds, whoever else is trying at the same time.
    // Not cancellable: a write that was cancelled can still have been applied, and would leave a lock nobody knows of
    private async ValueTask<bool> TryLockAsync(FilterDefinition<Entities.SagaState> filter, string lockId)
        => WasApplied(await WriteLockAsync(filter, lockId).ConfigureAwait(false));

    private async ValueTask<UpdateResult> WriteLockAsync(FilterDefinition<Entities.SagaState> filter, string lockId)
    {
        var takeLock = Builders<Entities.SagaState>.Update
            .Set(e => e.LockId, lockId)
            .Set(e => e.LockTime, DateTimeOffset.UtcNow);

        return await _dbContext.SagaStates
            .UpdateOneAsync(filter, takeLock, cancellationToken: CancellationToken.None)
            .ConfigureAwait(false);
    }

    // with an unacknowledged write concern the server does not say whether the update matched:
    // it is taken as applied, as an update whose result nobody reads would be
    private static bool WasApplied(UpdateResult result)
        => !result.IsAcknowledged || result.MatchedCount > 0;

    private async ValueTask InsertLockedAsync(ISagaInstance state, string lockId)
    {
        var entity = new Entities.SagaState()
        {
            Id = ObjectId.GenerateNewId(),
            CorrelationId = state.CorrelationId,
            InstanceId = state.InstanceId,
            IsCompleted = state.IsCompleted,
            SagaType = state.Descriptor.SagaType.FullName,
            SagaStateType = state.Descriptor.SagaStateType?.FullName,
            TriggerMessageId = state.TriggerMessageId,
            // stored with the saga, so that whoever finds it before its first message is done with can read it
            StateData = ExtractStateData(state),
            LockId = lockId,
            LockTime = DateTimeOffset.UtcNow,
        };

        try
        {
            await _dbContext.SagaStates.InsertOneAsync(entity, cancellationToken: CancellationToken.None)
                                       .ConfigureAwait(false);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // someone else started the saga for this correlation in the meantime, under another instance id:
            // whoever handles the message looks for the saga again and finds theirs
            throw new OptimisticLockException($"saga state for correlation '{state.CorrelationId}' was created by someone else");
        }
    }

    public ValueTask ReleaseAsync(ISagaInstance state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        return ReleaseAsyncCore(state, storeInstance: true, cancellationToken);
    }

    /// <summary>
    /// releases the saga. The saga itself stays as it was stored.
    /// </summary>
    public ValueTask AbandonAsync(ISagaInstance state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        return ReleaseAsyncCore(state, storeInstance: false, cancellationToken);
    }

    private async ValueTask ReleaseAsyncCore(ISagaInstance state, bool storeInstance, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filterBuilder = Builders<Entities.SagaState>.Filter;
        var heldByState = filterBuilder.And(
            WithInstanceId(state.InstanceId),
            filterBuilder.Eq(e => e.LockId, state.LockId));

        // the saga is unlocked by its lock time. Its lock id is replaced, not removed:
        // whoever found the saga before this release cannot lock it as it was then
        var release = Builders<Entities.SagaState>.Update
            .Set(e => e.LockTime, null)
            .Set(e => e.LockId, Guid.NewGuid().ToString());

        if (storeInstance)
            release = StoreInstance(release, state);

        // one conditional update: the saga is only written while the lock is still the one this state holds.
        // Not cancellable: cancelled in flight it can still be applied, and whoever called would undo
        // what goes with a release that did happen
        var result = await _dbContext.SagaStates.UpdateOneAsync(heldByState, release, cancellationToken: CancellationToken.None)
                                                .ConfigureAwait(false);
        if (WasApplied(result))
        {
            _foundSagas.Remove(state);
            return;
        }

        var exists = await _dbContext.SagaStates
            .Find(WithInstanceId(state.InstanceId))
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!exists)
            throw new ArgumentException($"saga state '{state.InstanceId}' not found");

        throw new LockException($"unable to release Saga State '{state.InstanceId}' with lock id '{state.LockId}'");
    }

    private UpdateDefinition<Entities.SagaState> StoreInstance(UpdateDefinition<Entities.SagaState> release, ISagaInstance state)
    {
        var processedMessages = state.ProcessedMessages
            .Select(msg => new Entities.SagaProcessedMessage()
            {
                InstanceId = state.InstanceId,
                MessageId = msg.MessageId,
                When = msg.When,
            })
            .ToArray();

        release = release
            .Set(e => e.IsCompleted, state.IsCompleted)
            .Set(e => e.ProcessedMessages, processedMessages);

        var stateData = ExtractStateData(state);
        return stateData is null
            ? release
            : release.Set(e => e.StateData, stateData);
    }

    private bool WasNotFound(ISagaInstance state)
    {
        var wasNotFound = _sagaNotFound == SagaKey(state.CorrelationId, state.Descriptor);
        if (wasNotFound)
            _sagaNotFound = null;
        return wasNotFound;
    }

    private static LockException AlreadyLocked(ISagaInstance state)
        => new($"saga state '{state.InstanceId}' is already locked");

    private static FilterDefinition<Entities.SagaState> WithInstanceId(string instanceId)
        => Builders<Entities.SagaState>.Filter.Eq(e => e.InstanceId, instanceId);

    private static string SagaKey(string correlationId, SagaDescriptor descriptor)
        => $"{correlationId}|{descriptor.SagaType.FullName}|{descriptor.SagaStateType?.FullName}";
}
