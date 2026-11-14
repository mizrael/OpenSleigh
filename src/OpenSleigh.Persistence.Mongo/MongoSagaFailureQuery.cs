using MongoDB.Driver;
using OpenSleigh.Queries;

namespace OpenSleigh.Persistence.Mongo;

public class MongoSagaFailureQuery : ISagaFailureQuery, ISagaFailurePurger
{
    private readonly IDbContext _dbContext;

    public MongoSagaFailureQuery(IDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async ValueTask<PagedResult<SagaMessageFailure>> GetAllAsync(
        SagaFailureQueryFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        filter ??= new SagaFailureQueryFilter();

        var mongoFilter = BuildFilter(filter);

        var totalCount = await _dbContext.SagaMessageFailures
            .CountDocumentsAsync(mongoFilter, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var entities = await _dbContext.SagaMessageFailures
            .Find(mongoFilter)
            .SortByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.FailureId)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Limit(filter.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<SagaMessageFailure>(
            entities.Select(entity => entity.ToFailure()).ToList(),
            (int)totalCount,
            filter.Page,
            filter.PageSize);
    }

    private static FilterDefinition<Entities.SagaFailureRecord> BuildFilter(SagaFailureQueryFilter filter)
    {
        var filterBuilder = Builders<Entities.SagaFailureRecord>.Filter;
        var mongoFilter = filterBuilder.Empty;

        if (filter.InstanceId is not null)
            mongoFilter &= filterBuilder.Eq(e => e.InstanceId, filter.InstanceId);

        if (filter.CorrelationId is not null)
            mongoFilter &= filterBuilder.Eq(e => e.CorrelationId, filter.CorrelationId);

        if (filter.SagaType is not null)
            mongoFilter &= filterBuilder.Eq(e => e.SagaType, filter.SagaType);

        if (filter.MessageType is not null)
            mongoFilter &= filterBuilder.Eq(e => e.MessageType, filter.MessageType);

        if (filter.Decision.HasValue)
            mongoFilter &= filterBuilder.Eq(e => e.Decision, filter.Decision.Value);

        return mongoFilter;
    }

    public async ValueTask<long> PurgeAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        var before = Builders<Entities.SagaFailureRecord>.Filter.Lt(e => e.OccurredAt, olderThan.UtcDateTime);

        var result = await _dbContext.SagaMessageFailures
            .DeleteManyAsync(before, cancellationToken)
            .ConfigureAwait(false);

        return result.IsAcknowledged ? result.DeletedCount : 0;
    }
}
