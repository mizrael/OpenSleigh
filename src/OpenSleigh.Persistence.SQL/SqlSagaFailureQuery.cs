using Microsoft.EntityFrameworkCore;
using OpenSleigh.Queries;

namespace OpenSleigh.Persistence.SQL;

public class SqlSagaFailureQuery : ISagaFailureQuery, ISagaFailurePurger
{
    private readonly SagaDbContext _dbContext;

    public SqlSagaFailureQuery(SagaDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async ValueTask<PagedResult<SagaMessageFailure>> GetAllAsync(
        SagaFailureQueryFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        filter ??= new SagaFailureQueryFilter();

        var query = _dbContext.SagaMessageFailures.AsNoTracking();

        if (filter.InstanceId is not null)
            query = query.Where(f => f.InstanceId == filter.InstanceId);

        if (filter.CorrelationId is not null)
            query = query.Where(f => f.CorrelationId == filter.CorrelationId);

        if (filter.SagaType is not null)
            query = query.Where(f => f.SagaType == filter.SagaType);

        if (filter.MessageType is not null)
            query = query.Where(f => f.MessageType == filter.MessageType);

        if (filter.Decision.HasValue)
            query = query.Where(f => f.Decision == filter.Decision.Value);

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var entities = await query
            .OrderByDescending(f => f.OccurredAt)
            .ThenByDescending(f => f.Id)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<SagaMessageFailure>(
            Items: entities.Select(entity => entity.ToFailure()).ToList(),
            TotalCount: totalCount,
            Page: filter.Page,
            PageSize: filter.PageSize);
    }

    public async ValueTask<long> PurgeAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        var before = olderThan.ToUniversalTime();

        return await _dbContext.SagaMessageFailures
            .Where(f => f.OccurredAt < before)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
