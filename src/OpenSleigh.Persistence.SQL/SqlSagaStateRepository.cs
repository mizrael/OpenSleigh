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

        // plain locals: a member chain inside the query would be evaluated by EF Core on every call
        var sagaType = descriptor.SagaType.FullName;
        var sagaStateType = descriptor.SagaStateType?.FullName;

        // what an earlier message left behind is of no use to this one
        _dbContext.ChangeTracker.Clear();
        _sagaNotFound = null;

        var entity = await _dbContext.SagaStates
            .Include(e => e.ProcessedMessages)
            .AsNoTracking()
            .FirstOrDefaultAsync(e =>
                e.CorrelationId == correlationId && 
                e.SagaType == sagaType &&
                e.SagaStateType == sagaStateType,
                cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            _sagaNotFound = SagaKey(correlationId, sagaType, sagaStateType);
            return null;
        }

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
            var state = ReadState(entity.StateData, descriptor.SagaStateType);
            var factory = _contextFactories.GetOrAdd(descriptor.SagaStateType!, static t =>
                (ISagaContextFactory)Activator.CreateInstance(
                    typeof(SagaContextFactory<>).MakeGenericType(t))!);
            result = factory.Create(state!, entity, descriptor);
        }

        if (entity.IsCompleted)
            result.MarkAsCompleted();

        RememberFoundSaga(result, entity);

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

    public ValueTask<string> LockAsync(ISagaInstance  state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        return TryGetFoundRow(state, out var foundRow)
            ? LockFoundSagaAsync(state, foundRow, cancellationToken)
            : LockAsyncCore(state, cancellationToken);
    }

    // locks a saga this repository knows nothing about, or stores a new one
    private async ValueTask<string> LockAsyncCore(ISagaInstance state, CancellationToken cancellationToken)
    {
        var sagaType = state.Descriptor.SagaType.FullName;
        var sagaStateType = state.Descriptor.SagaStateType?.FullName;

        // a saga this repository holds the lock of is not read again, and stays as it is when the lock is refused.
        // A saga this repository looked for and did not find is stored without looking again:
        // if someone else stored it in the meantime, the insert fails and is handled below
        var entity = FindTracked(state.InstanceId);
        var wasTracked = entity is not null;
        if (entity is null && !WasNotFound(state.CorrelationId, sagaType, sagaStateType))
            entity = await _dbContext.SagaStates
                .FirstOrDefaultAsync(e =>
                    (e.InstanceId == state.InstanceId || e.CorrelationId == state.CorrelationId) &&
                    e.SagaType == sagaType &&
                    e.SagaStateType == sagaStateType,
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
                SagaType = sagaType,
                SagaStateType = sagaStateType,
                TriggerMessageId = state.TriggerMessageId,
                // stored with the saga, so that whoever finds it before its first message is done with can read it
                StateData = ExtractStateData(state),
            };
            _dbContext.SagaStates.Add(entity);
        }
        else if (IsLocked(entity.LockId, entity.LockTime))
        {
            // read just now and left tracked, the row would be handed back as it is by a later attempt, locked for good
            if (!wasTracked)
                _dbContext.Entry(entity).State = EntityState.Detached;
            throw AlreadyLocked(state);
        }

        SetNewLock(entity);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken)
                            .ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // left tracked, the row would come back from a later attempt with the lock that was not stored
            ForgetUnsavedChanges(state);
            throw AlreadyLocked(state);
        }
        catch (DbUpdateException)
        {
            // the row that could not be stored would be sent again by every later save on this context
            ForgetUnsavedChanges(state);

            var storedInstanceId = await _dbContext.SagaStates
                .AsNoTracking()
                .Where(e =>
                    e.CorrelationId == state.CorrelationId &&
                    e.SagaType == sagaType &&
                    e.SagaStateType == sagaStateType)
                .Select(e => e.InstanceId)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (storedInstanceId is null)
                throw;

            // someone else stored the saga in the meantime. Under another instance id, the message has to look
            // for the saga again; under this one, they hold the lock they stored it with
            if (storedInstanceId != state.InstanceId)
                throw new OptimisticLockException();

            throw AlreadyLocked(state);
        }
        catch
        {
            ForgetUnsavedChanges(state);
            throw;
        }

        RememberLockedSaga(state, isNew);

        return entity.LockId!;
    }

    private SagaState? FindTracked(string instanceId)
        => _dbContext.SagaStates.Local.FirstOrDefault(e => e.InstanceId == instanceId);

    private bool IsLocked(string? lockId, DateTimeOffset? lockTime)
        => lockId is not null &&
           lockTime is not null &&
           lockTime > DateTimeOffset.UtcNow - _options.LockMaxDuration;

    private static void SetNewLock(SagaState entity)
    {
        entity.LockTime = DateTimeOffset.UtcNow;
        entity.LockId = NewLockId();
    }

    private static string NewLockId()
#if NET9_0_OR_GREATER
        => Guid.CreateVersion7().ToString();
#else
        => Guid.NewGuid().ToString();
#endif

    private static LockException AlreadyLocked(ISagaInstance state)
        => new($"saga state '{state.InstanceId}' is already locked");
}
