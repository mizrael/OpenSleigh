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

public partial class SqlSagaStateRepository : ISagaStateRepository
{
    private readonly SagaDbContext _dbContext;
    private readonly SqlSagaStateRepositoryOptions _options;
    private readonly ISerializer _serializer;

    private static readonly ConcurrentDictionary<Type, ISagaContextFactory> _contextFactories = new();
    private static readonly ConcurrentDictionary<Type, ISagaStateExtractor> _stateExtractors = new();

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

        RememberFoundSaga(entity);

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
            var factory = _contextFactories.GetOrAdd(descriptor.SagaStateType!, static t =>
                (ISagaContextFactory)Activator.CreateInstance(
                    typeof(SagaContextFactory<>).MakeGenericType(t))!);
            result = factory.Create(state!, entity, descriptor);
        }

        if (entity.IsCompleted)
            result.MarkAsCompleted();

        return result;
    }

    public ValueTask<string> LockAsync(ISagaInstance  state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        return LockAsyncCore(state, cancellationToken);
    }

    private async ValueTask<string> LockAsyncCore(ISagaInstance state, CancellationToken cancellationToken)
    {
        var lockId = await TryLockFoundSagaAsync(state, cancellationToken).ConfigureAwait(false);
        if (lockId is not null)
            return lockId;

        var entity = await _dbContext.SagaStates
            .FirstOrDefaultAsync(e =>
                (e.InstanceId == state.InstanceId || e.CorrelationId == state.CorrelationId) &&
                e.SagaType == state.Descriptor.SagaType.FullName &&
                ((state.Descriptor.SagaStateType == null && e.SagaStateType == null) ||
                 (state.Descriptor.SagaStateType != null && e.SagaStateType == state.Descriptor.SagaStateType.FullName)),
                cancellationToken)
            .ConfigureAwait(false);

        var isNew = entity is null;
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
            if (IsLocked(entity.LockId, entity.LockTime))
            {
                // left tracked, the row would be handed back as it is now by a later attempt, locked for good
                _dbContext.Entry(entity).State = EntityState.Detached;
                throw new LockException($"saga state '{state.InstanceId}' is already locked");
            }
        }

        SetNewLock(entity);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken)
                            .ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new LockException($"saga state '{state.InstanceId}' is already locked");
        }
        catch (DbUpdateException)
        {
            entity = await _dbContext.SagaStates
                .FirstOrDefaultAsync(e =>
                    e.CorrelationId == state.CorrelationId &&
                    e.SagaType == state.Descriptor.SagaType.FullName &&
                    ((state.Descriptor.SagaStateType == null && e.SagaStateType == null) ||
                     (state.Descriptor.SagaStateType != null && e.SagaStateType == state.Descriptor.SagaStateType.FullName)),
                    cancellationToken)
                .ConfigureAwait(false);

            if (entity != null && entity.InstanceId != state.InstanceId)
            {
                throw new OptimisticLockException();
            }

            throw;
        }

        if (isNew)
            RememberNewSaga(entity.InstanceId);

        return entity.LockId!;
    }

    private bool IsLocked(string? lockId, DateTimeOffset? lockTime)
        => lockId is not null &&
           lockTime is not null &&
           lockTime > DateTimeOffset.UtcNow - _options.LockMaxDuration;

    private static void SetNewLock(SagaState entity)
    {
        entity.LockTime = DateTimeOffset.UtcNow;
#if NET9_0_OR_GREATER
        entity.LockId = Guid.CreateVersion7().ToString();
#else
        entity.LockId = Guid.NewGuid().ToString();
#endif
    }
}
