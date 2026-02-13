using Microsoft.EntityFrameworkCore;
using OpenSleigh.Queries;
using OpenSleigh.Utils;

namespace OpenSleigh.Persistence.SQL;

public class SqlSagaStateQuery : ISagaStateQuery
{
    private readonly SagaDbContext _dbContext;
    private readonly ISerializer _serializer;

    public SqlSagaStateQuery(SagaDbContext dbContext, ISerializer serializer)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    public async ValueTask<SagaInstanceInfo?> GetByInstanceIdAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.SagaStates
            .Include(s => s.ProcessedMessages)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.InstanceId == instanceId, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : MapToSagaInstanceInfo(entity);
    }

    public async ValueTask<SagaInstanceInfo?> GetByCorrelationIdAsync(
        string correlationId,
        string sagaType,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.SagaStates
            .Include(s => s.ProcessedMessages)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.CorrelationId == correlationId && s.SagaType == sagaType, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : MapToSagaInstanceInfo(entity);
    }

    public async ValueTask<PagedResult<SagaInstanceInfo>> GetAllAsync(
        SagaQueryFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        filter ??= new SagaQueryFilter();

        IQueryable<Entities.SagaState> query = _dbContext.SagaStates
            .Include(s => s.ProcessedMessages)
            .AsNoTracking();

        if (filter.SagaType is not null)
            query = query.Where(s => s.SagaType == filter.SagaType);

        if (filter.IsCompleted.HasValue)
            query = query.Where(s => s.IsCompleted == filter.IsCompleted.Value);

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await query
            .OrderBy(s => s.InstanceId)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<SagaInstanceInfo>(
            Items: items.Select(MapToSagaInstanceInfo).ToList(),
            TotalCount: totalCount,
            Page: filter.Page,
            PageSize: filter.PageSize);
    }

    private SagaInstanceInfo MapToSagaInstanceInfo(Entities.SagaState entity)
    {
        object? stateData = null;
        if (entity.StateData is not null && entity.StateData.Length > 0 && entity.SagaStateType is not null)
        {
            var stateType = Type.GetType(entity.SagaStateType);
            if (stateType is not null)
            {
                try { stateData = _serializer.Deserialize(entity.StateData, stateType); }
                catch { /* corrupted or incompatible state data; surface as null */ }
            }
        }

        return new SagaInstanceInfo
        {
            InstanceId = entity.InstanceId,
            CorrelationId = entity.CorrelationId,
            TriggerMessageId = entity.TriggerMessageId,
            SagaType = entity.SagaType,
            SagaStateType = entity.SagaStateType,
            IsCompleted = entity.IsCompleted,
            IsLocked = entity.LockId is not null && entity.LockTime is not null,
            ProcessedMessages = entity.ProcessedMessages
                .Select(m => new ProcessedMessageInfo(m.MessageId, m.When))
                .ToList(),
            StateData = stateData
        };
    }
}
