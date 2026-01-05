using Microsoft.EntityFrameworkCore;
using OpenSleigh.Persistence.SQL.Entities;
using OpenSleigh.Transport;
using OpenSleigh.Utils;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace OpenSleigh.Persistence.SQL;

[ExcludeFromCodeCoverage]
public record SqlSagaStateRepositoryOptions(TimeSpan LockMaxDuration)
{
    public static readonly SqlSagaStateRepositoryOptions Default = new (TimeSpan.FromMinutes(1));
}

public class SqlSagaStateRepository : ISagaStateRepository
{
    private static readonly MethodInfo _createSagaContextMethod = typeof(SqlSagaStateRepository)
        .GetMethod(nameof(CreateSagaContextGeneric), BindingFlags.NonPublic | BindingFlags.Static)!;
    
    private static readonly MethodInfo _setStateDataMethod = typeof(SqlSagaStateRepository)
        .GetMethod(nameof(SetStateDataGeneric), BindingFlags.NonPublic | BindingFlags.Instance)!;

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
            var genericMethod = _createSagaContextMethod.MakeGenericMethod(descriptor.SagaStateType);
            result = (ISagaInstance)genericMethod.Invoke(null, new object[] { state!, entity, descriptor })!;
        }

        if (entity.IsCompleted)
            result.MarkAsCompleted();

        return result;
    }

    private static ISagaInstance<TS> CreateSagaContextGeneric<TS>(TS state, SagaState entity, SagaDescriptor descriptor)
        => new SagaInstance<TS>(
               instanceId: entity.InstanceId,
               triggerMessageId: entity.TriggerMessageId,
               correlationId: entity.CorrelationId,
               descriptor: descriptor,
               state: state,
               processedMessages: entity.ProcessedMessages.Select(e => new ProcessedMessage()
               {
                   MessageId = e.MessageId,
                   When = e.When
               }));

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
            var genericMethod = _setStateDataMethod.MakeGenericMethod(state.Descriptor.SagaStateType);
            genericMethod.Invoke(this, new object[] { state, entity });
        }

        await _dbContext.SaveChangesAsync(cancellationToken)
                    .ConfigureAwait(false);
    }

    private void SetStateDataGeneric<TS>(ISagaInstance<TS> state, SagaState entity)
    {
        entity.StateData = _serializer.Serialize(state.State);
    }
}
