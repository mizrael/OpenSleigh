using Microsoft.EntityFrameworkCore;
using OpenSleigh.Outbox;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Persistence.SQL.Entities;
using OpenSleigh.Utils;

namespace OpenSleigh.Persistence.PostgreSQL;

internal class PostgreSQLOutboxRepository : SqlOutboxRepository
{
    private const string Table = $"{Constants.DbSchema}.\"{OutboxMessage.TableName}\"";
    private const string DeliverAt = $"\"{nameof(OutboxMessage.DeliverAt)}\"";

    // two ranges of the index on the due time: the delayed messages that are due, and the messages without a delay.
    // A row that is waiting for its time is never read, however many there are. Each range is read, and its rows
    // locked, on its own: PostgreSQL does not lock the rows of a union.
    // The delayed ones are asked for in the order of the index, the longest due first. The others are not asked for
    // in any order: an order the planner cannot tell from the index makes it sort all of them at every pass.
    // They get what the delayed ones leave of a pass, so that no row is locked without being returned.
    // The due time is sent by the application and compared with its own clock, like every other time in the outbox
    private const string ReadQuery =
        $"WITH due AS (SELECT * FROM {Table} WHERE {DeliverAt} <= {{0}} ORDER BY {DeliverAt} LIMIT {{2}} FOR UPDATE SKIP LOCKED), " +
        $"immediate AS (SELECT * FROM {Table} WHERE {DeliverAt} IS NULL LIMIT ({{1}} - (SELECT count(*) FROM due)) FOR UPDATE SKIP LOCKED) " +
        "SELECT * FROM due UNION ALL SELECT * FROM immediate";

    public PostgreSQLOutboxRepository(
        SqlOutboxRepositoryOptions options, 
        SagaDbContext dbContext, 
        ITypeResolver typeResolver, 
        ISerializer serializer, 
        DuplicateKeyDetector duplicateKeyDetector,
        Func<SagaDbContext>? relayDbContext = null) : base(options, dbContext, typeResolver, serializer, duplicateKeyDetector, relayDbContext)
    {
    }

    protected override string ReadPendingQuery => ReadQuery;
}