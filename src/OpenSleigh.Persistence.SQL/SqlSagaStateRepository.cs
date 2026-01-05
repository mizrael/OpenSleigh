using Microsoft.EntityFrameworkCore;
using OpenSleigh.Persistence.SQL.Entities;
using OpenSleigh.Transport;
using OpenSleigh.Utils;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Persistence.SQL;

[ExcludeFromCodeCoverage]
public record SqlSagaStateRepositoryOptions(TimeSpan LockMaxDuration)
{
    public static readonly SqlSagaStateRepositoryOptions Default = new (TimeSpan.FromMinutes(1));
}

public class SqlSagaStateRepository : ISagaStateRepository
{
    private static readonly ConcurrentDictionary<Type, ISagaContextFactory> _contextFactories = new();
    private static readonly ConcurrentDictionary<Type, IStateDataSetter> _stateDataSetters = new();

    private readonly SagaDbContext _dbContext;
    private readonly SqlSagaStateRepositoryOptions _options;
    private readonly ISerializer _serializer;

    public SqlSagaStateRepository(SagaDbContext dbContext, SqlSagaStateRepositoryOptions options, ISerializer serializer)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    public async ValueTask<ISagaInstance ?> FindAsync<TM>(SagaDescriptor descriptor, IMessageContext<TM> messageContext, CancellationToken cancellationToken = default)
        where TM : IMessage
    { 
        var correlationId = messageContext.CorrelationId;

        var entity = await _dbContext.SagaStates
            .Include(e => e.ProcessedMessages)
            .AsNoTracking()
            .FirstOrDefaultAsync(e =>
                e.CorrelationId == correlationId && 
                e.SagaType == descriptor.SagaType.FullName &&
                ((descriptor.SagaStateType == null && e.SagaStateType == null) || 
                (descriptor.SagaStateType != null && e.SagaStateType == descriptor.SagaStateType.FullName)),
                cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
            return null;

        ISagaInstance ? result;

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

    public ValueTask<string> LockAsync(ISagaInstance  state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        return LockAsyncCore(state, cancellationToken);
    }

    private async ValueTask<string> LockAsyncCore(ISagaInstance  state, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.SagaStates
            .Include(e => e.ProcessedMessages)
            .FirstOrDefaultAsync(e => e.InstanceId == state.InstanceId, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            entity = new SagaState()
            {
                CorrelationId = state.CorrelationId,
                InstanceId = state.InstanceId,
                IsCompleted = state.IsCompleted,
                SagaType = state.Descriptor.SagaType.FullName,
                SagaStateType = state.Descriptor.SagaStateType?.FullName,
                TriggerMessageId = state.TriggerMessageId,                    
            };
            _dbContext.SagaStates.Add(entity);
        }
        else
        {
            if (entity.LockId is not null &&
                entity.LockTime is not null &&
                entity.LockTime > DateTimeOffset.UtcNow - _options.LockMaxDuration)
                throw new LockException($"saga state '{state.InstanceId}' is already locked");               
        }

        entity.LockTime = DateTimeOffset.UtcNow;
#if NET9_0_OR_GREATER
        entity.LockId = Guid.CreateVersion7().ToString();
#else
        entity.LockId = Guid.NewGuid().ToString();
#endif

        await _dbContext.SaveChangesAsync(cancellationToken)
                        .ConfigureAwait(false);

        return entity.LockId;
    }

    public ValueTask ReleaseAsync(ISagaInstance  state, CancellationToken cancellationToken = default)
    {  
        ArgumentNullException.ThrowIfNull(state);

        return ReleaseAsyncCore(state, cancellationToken);
    }

    private async ValueTask ReleaseAsyncCore(ISagaInstance  state, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.SagaStates
             .Include(e => e.ProcessedMessages)
             .FirstOrDefaultAsync(e => e.InstanceId == state.InstanceId, cancellationToken)
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
            entity.ProcessedMessages.Add(new SagaProcessedMessage()
            {
                InstanceId = state.InstanceId,
                MessageId = msg.MessageId,
                When = msg.When,
                SagaState = entity
            });
                    
        if (state.GetType().IsGenericType && state.Descriptor.SagaStateType is not null)
        {
            var setter = _stateDataSetters.GetOrAdd(state.Descriptor.SagaStateType, CreateStateDataSetter);
            setter.SetStateData(state, entity, _serializer);
        }

        await _dbContext.SaveChangesAsync(cancellationToken)
                    .ConfigureAwait(false);
    }

    private interface ISagaContextFactory
    {
        ISagaInstance Create(object state, SagaState entity, SagaDescriptor descriptor);
    }

    private sealed class SagaContextFactory<TS> : ISagaContextFactory
    {
        public ISagaInstance Create(object state, SagaState entity, SagaDescriptor descriptor)
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
        void SetStateData(ISagaInstance state, SagaState entity, ISerializer serializer);
    }

    private sealed class StateDataSetter<TS> : IStateDataSetter
    {
        public void SetStateData(ISagaInstance state, SagaState entity, ISerializer serializer)
        {
            var typedState = (ISagaInstance<TS>)state;
            entity.StateData = serializer.Serialize(typedState.State);
        }
    }
}
