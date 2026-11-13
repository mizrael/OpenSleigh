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

    // two ranges of the index on the due time, each ordered by it so that the planner takes the rows from the index
    // whatever it knows about the table: the delayed messages that are due, and the messages without a delay.
    // A row that is waiting for its time is never read, however many there are.
    // The due time is sent by the application and compared with its own clock, like every other time in the outbox
    private const string ReadQuery =
        "SELECT * FROM (" +
        $"(SELECT * FROM {Table} WHERE {DeliverAt} <= {{0}} ORDER BY {DeliverAt} LIMIT {{2}} FOR UPDATE SKIP LOCKED) " +
        "UNION ALL " +
        $"(SELECT * FROM {Table} WHERE {DeliverAt} IS NULL ORDER BY {DeliverAt} LIMIT {{1}} FOR UPDATE SKIP LOCKED)" +
        ") AS pending " +
        $"ORDER BY CASE WHEN {DeliverAt} IS NULL THEN 1 ELSE 0 END, {DeliverAt} LIMIT {{1}}";

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