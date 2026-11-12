using MongoDB.Bson;
using MongoDB.Driver;
using OpenSleigh.Transport;
using OpenSleigh.Utils;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

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

    // sagas this repository looked for and did not find: locking one of them stores it, without looking again
    private readonly ConcurrentDictionary<string, byte> _sagasNotFound = new();

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

        var entity = await _dbContext.SagaStates.FindOneAsync(filter, cancellationToken)
                                     .ConfigureAwait(false);

        if (entity is null)
        {
            _sagasNotFound.TryAdd(SagaKey(correlationId, descriptor), 0);
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
            var state = _serializer.Deserialize(entity.StateData, descriptor.SagaStateType);
            var factory = _contextFactories.GetOrAdd(descriptor.SagaStateType!, static t =>
                (ISagaContextFactory)Activator.CreateInstance(
                    typeof(SagaContextFactory<>).MakeGenericType(t))!);
            result = factory.Create(state!, entity, descriptor);
        }

        if (entity.IsCompleted)
            result.MarkAsCompleted();

        return result;
    }

    public ValueTask<string> LockAsync(ISagaInstance state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        return LockAsyncCore(state, cancellationToken);
    }

    private async ValueTask<string> LockAsyncCore(ISagaInstance state, CancellationToken cancellationToken)
    {
        var lockId = Guid.NewGuid().ToString();

        // a saga this repository looked for and did not find is new: it is stored locked, in one write.
        // If someone else stored it in the meantime, it is treated like any other existing saga
        if (_sagasNotFound.TryRemove(SagaKey(state.CorrelationId, state.Descriptor), out _))
        {
            try
            {
                await InsertLockedAsync(state, lockId, cancellationToken).ConfigureAwait(false);
                return lockId;
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
            }
        }

        if (await TryLockExistingAsync(state, lockId, cancellationToken).ConfigureAwait(false))
            return lockId;

        var exists = await _dbContext.SagaStates
            .Find(WithInstanceId(state.InstanceId))
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);
        if (exists)
            throw new LockException($"saga state '{state.InstanceId}' is already locked");

        await InsertLockedAsync(state, lockId, cancellationToken).ConfigureAwait(false);
        return lockId;
    }

    // one conditional update: the lock is taken only if nobody holds it, whoever else is trying at the same time
    private async ValueTask<bool> TryLockExistingAsync(ISagaInstance state, string lockId, CancellationToken cancellationToken)
    {
        var filterBuilder = Builders<Entities.SagaState>.Filter;
        var lockExpiredBefore = DateTimeOffset.UtcNow - _options.LockMaxDuration;
        var notLocked = filterBuilder.Or(
            filterBuilder.Eq(e => e.LockId, null),
            filterBuilder.Eq(e => e.LockTime, null),
            filterBuilder.Lte(e => e.LockTime, lockExpiredBefore));

        var takeLock = Builders<Entities.SagaState>.Update
            .Set(e => e.LockId, lockId)
            .Set(e => e.LockTime, DateTimeOffset.UtcNow);

        var result = await _dbContext.SagaStates
            .UpdateOneAsync(filterBuilder.And(WithInstanceId(state.InstanceId), notLocked), takeLock, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.MatchedCount > 0;
    }

    private async ValueTask InsertLockedAsync(ISagaInstance state, string lockId, CancellationToken cancellationToken)
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
            LockId = lockId,
            LockTime = DateTimeOffset.UtcNow,
        };

        await _dbContext.SagaStates.InsertOneAsync(entity, cancellationToken: cancellationToken)
                                   .ConfigureAwait(false);
    }

    public async ValueTask ReleaseAsync(ISagaInstance state, CancellationToken cancellationToken = default)
    {
        var filterBuilder = Builders<Entities.SagaState>.Filter;
        var heldByState = filterBuilder.And(
            WithInstanceId(state.InstanceId),
            filterBuilder.Eq(e => e.LockId, state.LockId));

        var processedMessages = state.ProcessedMessages
            .Select(msg => new Entities.SagaProcessedMessage()
            {
                InstanceId = state.InstanceId,
                MessageId = msg.MessageId,
                When = msg.When,
            })
            .ToArray();

        var release = Builders<Entities.SagaState>.Update
            .Set(e => e.LockTime, null)
            .Set(e => e.LockId, null)
            .Set(e => e.IsCompleted, state.IsCompleted)
            .Set(e => e.ProcessedMessages, processedMessages);

        // the lock is checked by the update itself, so the state is not required to be complete before it is known to hold it
        var stateType = state.Descriptor?.SagaStateType;
        if (stateType is not null && state.GetType().IsGenericType)
        {
            var extractor = _stateExtractors.GetOrAdd(stateType, static t =>
                (ISagaStateExtractor)Activator.CreateInstance(
                    typeof(SagaStateExtractor<>).MakeGenericType(t))!);
            release = release.Set(e => e.StateData, extractor.Extract(state, _serializer));
        }

        // one conditional update: the saga is only written while the lock is still the one this state holds
        var result = await _dbContext.SagaStates.UpdateOneAsync(heldByState, release)
                                                .ConfigureAwait(false);
        if (result.MatchedCount > 0)
            return;

        var exists = await _dbContext.SagaStates
            .Find(WithInstanceId(state.InstanceId))
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!exists)
            throw new ArgumentException($"saga state '{state.InstanceId}' not found");

        throw new LockException($"unable to release Saga State '{state.InstanceId}' with lock id '{state.LockId}'");
    }

    private static FilterDefinition<Entities.SagaState> WithInstanceId(string instanceId)
        => Builders<Entities.SagaState>.Filter.Eq(e => e.InstanceId, instanceId);

    private static string SagaKey(string correlationId, SagaDescriptor descriptor)
        => $"{correlationId}|{descriptor.SagaType.FullName}|{descriptor.SagaStateType?.FullName}";
}
