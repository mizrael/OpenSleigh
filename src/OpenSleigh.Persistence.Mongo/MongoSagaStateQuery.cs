using MongoDB.Driver;
using OpenSleigh.Persistence.Mongo.Entities;
using OpenSleigh.Queries;
using OpenSleigh.Utils;

namespace OpenSleigh.Persistence.Mongo;

// SPIKE, not for merging: reads the documents of the ledger experiment. A list of sagas reads every document:
// the design has a summary collection for it, which this experiment does not build.
public class MongoSagaStateQuery : ISagaStateQuery
{
    private readonly IDbContext _dbContext;
    private readonly ISerializer _serializer;
    private readonly ISagaStateTypeResolver? _stateTypes;

    public MongoSagaStateQuery(IDbContext dbContext, ISerializer serializer, ISagaStateTypeResolver? stateTypes = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(serializer);
        _dbContext = dbContext;
        _serializer = serializer;
        _stateTypes = stateTypes;
    }

    public async ValueTask<SagaInstanceInfo?> GetByInstanceIdAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        var entries = await ReadAsync(Builders<SagaLedgerEntry>.Filter.Eq(e => e.InstanceId, instanceId), cancellationToken)
            .ConfigureAwait(false);

        return entries.Count == 0 ? null : Map(entries);
    }

    public async ValueTask<SagaInstanceInfo?> GetByCorrelationIdAsync(
        string correlationId,
        string sagaType,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<SagaLedgerEntry>.Filter;
        var entries = await ReadAsync(
            filter.And(filter.Eq(e => e.CorrelationId, correlationId), filter.Eq(e => e.SagaType, sagaType)),
            cancellationToken).ConfigureAwait(false);

        return entries.Count == 0 ? null : Map(entries);
    }

    public async ValueTask<PagedResult<SagaInstanceInfo>> GetAllAsync(
        SagaQueryFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        filter ??= new SagaQueryFilter();

        var mongoFilter = filter.SagaType is null
            ? Builders<SagaLedgerEntry>.Filter.Empty
            : Builders<SagaLedgerEntry>.Filter.Eq(e => e.SagaType, filter.SagaType);

        var sagas = (await ReadAsync(mongoFilter, cancellationToken).ConfigureAwait(false))
            .GroupBy(e => e.InstanceId)
            .Select(entries => Map(entries.ToList()))
            .Where(saga => filter.IsCompleted is null || saga.IsCompleted == filter.IsCompleted)
            .OrderBy(saga => saga.InstanceId)
            .ToList();

        var items = sagas
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToList()
            .AsReadOnly();

        return new PagedResult<SagaInstanceInfo>(items, sagas.Count, filter.Page, filter.PageSize);
    }

    private async ValueTask<List<SagaLedgerEntry>> ReadAsync(FilterDefinition<SagaLedgerEntry> filter, CancellationToken cancellationToken)
        => await _dbContext.SagaLedger
            .Find(filter)
            .SortBy(e => e.Version)
            .Project<SagaLedgerEntry>(Builders<SagaLedgerEntry>.Projection.Exclude(e => e.Outbox))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    // the entries of one saga, oldest first
    private SagaInstanceInfo Map(List<SagaLedgerEntry> entries)
    {
        var latest = entries[^1];

        object? stateData = null;
        if (latest.StateData is not null && latest.StateData.Length > 0 && latest.SagaStateType is not null)
        {
            var stateType = _stateTypes?.Resolve(latest.SagaStateType);
            if (stateType is not null)
            {
                try { stateData = _serializer.Deserialize(latest.StateData, stateType); }
                catch { /* corrupted or incompatible state data; surface as null */ }
            }
        }

        return new SagaInstanceInfo
        {
            InstanceId = latest.InstanceId,
            CorrelationId = latest.CorrelationId,
            TriggerMessageId = latest.TriggerMessageId,
            SagaType = latest.SagaType,
            SagaStateType = latest.SagaStateType,
            IsCompleted = latest.IsCompleted,
            IsLocked = latest.LeaseId is not null && latest.LeaseTime is not null,
            ProcessedMessages = entries
                .SelectMany(entry => entry.MessageIds.Select(id =>
                    new ProcessedMessageInfo(id, new DateTimeOffset(DateTime.SpecifyKind(entry.OccurredAt, DateTimeKind.Utc)))))
                .ToList()
                .AsReadOnly(),
            StateData = stateData
        };
    }
}
