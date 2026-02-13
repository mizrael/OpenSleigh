using MongoDB.Driver;
using OpenSleigh.Queries;
using OpenSleigh.Utils;

namespace OpenSleigh.Persistence.Mongo;

public class MongoSagaStateQuery : ISagaStateQuery
{
    private readonly IDbContext _dbContext;
    private readonly ISerializer _serializer;

    public MongoSagaStateQuery(IDbContext dbContext, ISerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(serializer);
        _dbContext = dbContext;
        _serializer = serializer;
    }

    public async ValueTask<SagaInstanceInfo?> GetByInstanceIdAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<Entities.SagaState>.Filter
            .Eq(e => e.InstanceId, instanceId);

        var entity = await _dbContext.SagaStates
            .FindOneAsync(filter, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : MapToSagaInstanceInfo(entity);
    }

    public async ValueTask<SagaInstanceInfo?> GetByCorrelationIdAsync(
        string correlationId,
        string sagaType,
        CancellationToken cancellationToken = default)
    {
        var filterBuilder = Builders<Entities.SagaState>.Filter;
        var filter = filterBuilder.And(
            filterBuilder.Eq(e => e.CorrelationId, correlationId),
            filterBuilder.Eq(e => e.SagaType, sagaType));

        var entity = await _dbContext.SagaStates
            .FindOneAsync(filter, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : MapToSagaInstanceInfo(entity);
    }

    public async ValueTask<PagedResult<SagaInstanceInfo>> GetAllAsync(
        SagaQueryFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        filter ??= new SagaQueryFilter();

        var filterBuilder = Builders<Entities.SagaState>.Filter;
        var mongoFilter = filterBuilder.Empty;

        if (filter.SagaType is not null)
            mongoFilter &= filterBuilder.Eq(e => e.SagaType, filter.SagaType);

        if (filter.IsCompleted.HasValue)
            mongoFilter &= filterBuilder.Eq(e => e.IsCompleted, filter.IsCompleted.Value);

        var totalCount = await _dbContext.SagaStates
            .CountDocumentsAsync(mongoFilter, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var skip = (filter.Page - 1) * filter.PageSize;

        var findOptions = new FindOptions<Entities.SagaState>
        {
            Skip = skip,
            Limit = filter.PageSize
        };

        var cursor = await _dbContext.SagaStates
            .FindAsync(mongoFilter, findOptions, cancellationToken)
            .ConfigureAwait(false);

        var entities = await cursor
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = entities
            .Select(MapToSagaInstanceInfo)
            .ToList()
            .AsReadOnly();

        return new PagedResult<SagaInstanceInfo>(items, (int)totalCount, filter.Page, filter.PageSize);
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
                .ToList()
                .AsReadOnly(),
            StateData = stateData
        };
    }
}
