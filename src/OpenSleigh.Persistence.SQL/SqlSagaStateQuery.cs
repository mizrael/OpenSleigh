using Microsoft.EntityFrameworkCore;
using OpenSleigh.Queries;
using OpenSleigh.Utils;

namespace OpenSleigh.Persistence.SQL;

// SPIKE, not for merging: reads the head and the ledger entries of the experiment.
public class SqlSagaStateQuery : ISagaStateQuery
{
    private readonly SagaDbContext _dbContext;
    private readonly ISerializer _serializer;
    private readonly ISagaStateTypeResolver? _stateTypes;

    public SqlSagaStateQuery(SagaDbContext dbContext, ISerializer serializer, ISagaStateTypeResolver? stateTypes = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _stateTypes = stateTypes;
    }

    public async ValueTask<SagaInstanceInfo?> GetByInstanceIdAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Sagas
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.InstanceId == instanceId, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : await MapAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<SagaInstanceInfo?> GetByCorrelationIdAsync(
        string correlationId,
        string sagaType,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Sagas
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.CorrelationId == correlationId && s.SagaType == sagaType, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : await MapAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<PagedResult<SagaInstanceInfo>> GetAllAsync(
        SagaQueryFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        filter ??= new SagaQueryFilter();

        IQueryable<Entities.SagaHead> query = _dbContext.Sagas.AsNoTracking();

        if (filter.SagaType is not null)
            query = query.Where(s => s.SagaType == filter.SagaType);

        if (filter.IsCompleted.HasValue)
            query = query.Where(s => s.IsCompleted == filter.IsCompleted.Value);

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var heads = await query
            .OrderBy(s => s.InstanceId)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = new List<SagaInstanceInfo>(heads.Count);
        foreach (var head in heads)
            items.Add(await MapAsync(head, cancellationToken).ConfigureAwait(false));

        return new PagedResult<SagaInstanceInfo>(
            Items: items,
            TotalCount: totalCount,
            Page: filter.Page,
            PageSize: filter.PageSize);
    }

    private async ValueTask<SagaInstanceInfo> MapAsync(Entities.SagaHead entity, CancellationToken cancellationToken)
    {
        var entries = await _dbContext.SagaLedger
            .AsNoTracking()
            .Where(e => e.InstanceId == entity.InstanceId)
            .OrderBy(e => e.Version)
            .Select(e => new { e.MessageId, e.OccurredAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        object? stateData = null;
        if (entity.StateData is not null && entity.StateData.Length > 0 && entity.SagaStateType is not null)
        {
            var stateType = _stateTypes?.Resolve(entity.SagaStateType);
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
            IsLocked = entity.LeaseId is not null && entity.LeaseTime is not null,
            ProcessedMessages = entries
                .Select(m => new ProcessedMessageInfo(m.MessageId, m.OccurredAt))
                .ToList(),
            StateData = stateData
        };
    }
}
