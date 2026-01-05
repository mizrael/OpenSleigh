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

public class MongoSagaStateRepository : ISagaStateRepository
{
    private static readonly ConcurrentDictionary<Type, ISagaContextFactory> _contextFactories = new();
    private static readonly ConcurrentDictionary<Type, IStateDataSetter> _stateDataSetters = new();

    private readonly IDbContext _dbContext;
    private readonly MongoSagaStateRepositoryOptions _options;
    private readonly ISerializer _serializer;

    public MongoSagaStateRepository(
        IDbContext dbContext,
        MongoSagaStateRepositoryOptions? options,
        ISerializer serializer)
    {
        _dbContext = dbContext;
        _options = options ?? MongoSagaStateRepositoryOptions.Default;
        _serializer = serializer;
    }

    private static ISagaContextFactory CreateContextFactory(Type stateType)
    {
        var factoryType = typeof(SagaContextFactory<>).MakeGenericType(stateType);
        return (ISagaContextFactory)Activator.CreateInstance(factoryType)!;
    }

    private static IStateDataSetter CreateStateDataSetter(Type stateType)
    {
        var setterType = typeof(StateDataSetter<>).MakeGenericType(stateType);
        return (IStateDataSetter)Activator.CreateInstance(setterType)!;
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
            return null;

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
            var factory = _contextFactories.GetOrAdd(descriptor.SagaStateType, CreateContextFactory);
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

        var filterBuilder = Builders<Entities.SagaState>.Filter;
        var filter = filterBuilder.Eq(e => e.InstanceId, state.InstanceId);
        var entity = await _dbContext.SagaStates.FindOneAsync(filter, cancellationToken)
                                                .ConfigureAwait(false);

        if (entity is null)
        {
            entity = new Entities.SagaState()
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
        }
        else
        {
            if (entity.LockId is not null &&
                entity.LockTime is not null &&
                entity.LockTime > DateTimeOffset.UtcNow - _options.LockMaxDuration)
                throw new LockException($"saga state '{state.InstanceId}' is already locked");
        }

        entity.LockTime = DateTimeOffset.UtcNow;
        entity.LockId = lockId;

        await _dbContext.SagaStates.ReplaceOneAsync(filter, entity, new ReplaceOptions()
        {
            IsUpsert = true,
        }).ConfigureAwait(false);

        return entity.LockId;
    }

    public async ValueTask ReleaseAsync(ISagaInstance state, CancellationToken cancellationToken = default)
    {
        var filterBuilder = Builders<Entities.SagaState>.Filter;
        var filter = filterBuilder.Eq(e => e.InstanceId, state.InstanceId);
        var entity = await _dbContext.SagaStates.FindOneAsync(filter, cancellationToken)
                                                .ConfigureAwait(false);
        if (entity is null)
            throw new ArgumentException($"saga state '{state.InstanceId}' not found");

        if (entity.LockId != state.LockId)
            throw new LockException($"unable to release Saga State '{state.InstanceId}' with lock id '{state.LockId}'");

        entity.LockTime = null;
        entity.LockId = null;

        entity.IsCompleted = state.IsCompleted;

        entity.ProcessedMessages.Clear();

        foreach (var msg in state.ProcessedMessages)
            entity.ProcessedMessages.Add(new Entities.SagaProcessedMessage()
            {
                InstanceId = state.InstanceId,
                MessageId = msg.MessageId,
                When = msg.When,
            });

        if (state.GetType().IsGenericType && state.Descriptor.SagaStateType is not null)
        {
            var setter = _stateDataSetters.GetOrAdd(state.Descriptor.SagaStateType, CreateStateDataSetter);
            setter.SetStateData(state, entity, _serializer);
        }

        await _dbContext.SagaStates.ReplaceOneAsync(filter, entity, new ReplaceOptions()
        {
            IsUpsert = false,
        }).ConfigureAwait(false);
    }

    private interface ISagaContextFactory
    {
        ISagaInstance Create(object state, Entities.SagaState entity, SagaDescriptor descriptor);
    }

    private sealed class SagaContextFactory<TS> : ISagaContextFactory
    {
        public ISagaInstance Create(object state, Entities.SagaState entity, SagaDescriptor descriptor)
            => new SagaInstance<TS>(
                   instanceId: entity.InstanceId,
                   triggerMessageId: entity.TriggerMessageId,
                   correlationId: entity.CorrelationId,
                   descriptor: descriptor,
                   state: (TS)state,
                   processedMessages: entity.ProcessedMessages.Select(e => new ProcessedMessage()
                   {
                       MessageId = e.MessageId,
                       When = e.When
                   }));
    }

    private interface IStateDataSetter
    {
        void SetStateData(ISagaInstance state, Entities.SagaState entity, ISerializer serializer);
    }

    private sealed class StateDataSetter<TS> : IStateDataSetter
    {
        public void SetStateData(ISagaInstance state, Entities.SagaState entity, ISerializer serializer)
        {
            var typedState = (ISagaInstance<TS>)state;
            entity.StateData = serializer.Serialize(typedState.State);
        }
    }
}
