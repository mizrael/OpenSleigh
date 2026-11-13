using Microsoft.EntityFrameworkCore;
using OpenSleigh.Outbox;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Persistence.SQL.Entities;
using OpenSleigh.Utils;

namespace OpenSleigh.Persistence.PostgreSQL;

internal class PostgreSQLOutboxRepository : SqlOutboxRepository
{
    // the due time is sent by the application and compared with its own clock, like every other time in the outbox.
    // Ordered by due time, the rows come from the index on it: a read stops at the first row that is not due,
    // however many of those there are, whatever the planner knows about the table
    private const string ReadQuery =
        $"SELECT * FROM {Constants.DbSchema}.\"{OutboxMessage.TableName}\" " +
        $"WHERE \"{nameof(OutboxMessage.DeliverAt)}\" <= {{0}} " +
        $"ORDER BY \"{nameof(OutboxMessage.DeliverAt)}\" LIMIT {{1}} FOR UPDATE SKIP LOCKED";

    public PostgreSQLOutboxRepository(
        SqlOutboxRepositoryOptions options, 
        SagaDbContext dbContext, 
        ITypeResolver typeResolver, 
        ISerializer serializer, 
        DuplicateKeyDetector duplicateKeyDetector,
        Func<SagaDbContext>? relayDbContext = null) : base(options, dbContext, typeResolver, serializer, duplicateKeyDetector, relayDbContext)
    {
    }

    public override async ValueTask<IEnumerable<MessageEnvelope>> ReadPendingAsync(CancellationToken cancellationToken = default)
    {
        var entities = await RelayDbContext.OutboxMessages
           .FromSqlRaw(ReadQuery, DateTimeOffset.UtcNow, _options.MaxMessagesToPull)
           .AsNoTracking()
           .ToListAsync(cancellationToken);

        var messages = new List<MessageEnvelope>(entities.Count);
        foreach (var entity in entities)
        {
            if (entity.TryMap(_typeResolver, _serializer, out var m))
                messages.Add(m);
        }
        return messages;
    }
}