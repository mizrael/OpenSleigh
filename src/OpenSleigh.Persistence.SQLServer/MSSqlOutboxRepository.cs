using Microsoft.EntityFrameworkCore;
using OpenSleigh.Outbox;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Persistence.SQL.Entities;
using OpenSleigh.Utils;

namespace OpenSleigh.Persistence.SQLServer;

internal class MSSqlOutboxRepository : SqlOutboxRepository, IOutboxReturnRepository
{
    private const string Table = $"[{Constants.DbSchema}].[{OutboxMessage.TableName}]";
    private const string MessageId = $"[{nameof(OutboxMessage.MessageId)}]";
    private const string DeliverAt = $"[{nameof(OutboxMessage.DeliverAt)}]";
    private const string Hints = $"WITH (UPDLOCK, READPAST, INDEX([{OutboxMessage.DeliverAtIndexName}]))";

    // two ranges of the index on the due time: the delayed messages that are due, and the messages without a delay.
    // A row that is waiting for its time is never read, however many there are. The index is named: left to choose,
    // SQL Server may scan the table, or sort it and lock every due row, depending on what it knew about the table
    // when it compiled the query. The messages of a pass are chosen, and locked, in the index alone, which has their key:
    // only the ones that are returned are looked up in the table, by key and one at a time, and their bodies are not sorted.
    // Without that being asked for, SQL Server may read the whole table to find them, and wait on every row
    // that is being stored. The due time is sent by the application and compared with its own clock,
    // like every other time in the outbox. The rows that are returned are locked as well as their index entries,
    // and skipped when someone holds them: whoever puts a returned message back goes for the row first,
    // and waits there for this pass to end instead of holding the row while it waits for the index entry
    private const string ReadQuery =
        "SELECT message.* FROM (" +
        $"SELECT TOP ({{1}}) pending.{MessageId} FROM (" +
        $"SELECT {MessageId}, {DeliverAt} FROM (SELECT TOP ({{2}}) {MessageId}, {DeliverAt} FROM {Table} {Hints} WHERE {DeliverAt} <= {{0}} ORDER BY {DeliverAt}) AS due " +
        "UNION ALL " +
        $"SELECT {MessageId}, {DeliverAt} FROM (SELECT TOP ({{1}}) {MessageId}, {DeliverAt} FROM {Table} {Hints} WHERE {DeliverAt} IS NULL) AS immediate" +
        ") AS pending " +
        $"ORDER BY CASE WHEN pending.{DeliverAt} IS NULL THEN 1 ELSE 0 END, pending.{DeliverAt}" +
        ") AS chosen " +
        $"INNER LOOP JOIN {Table} AS message WITH (FORCESEEK, UPDLOCK, READPAST) ON message.{MessageId} = chosen.{MessageId}";

    public MSSqlOutboxRepository(
        SagaDbContext dbContext, 
        ITypeResolver typeResolver, 
        SqlOutboxRepositoryOptions options, 
        ISerializer serializer, 
        DuplicateKeyDetector duplicateKeyDetector,
        Func<SagaDbContext>? relayDbContext = null) : base(options, dbContext, typeResolver, serializer, duplicateKeyDetector, relayDbContext)
    {
    }

    protected override string ReadPendingQuery => ReadQuery;

    public ValueTask PutBackAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
        => PutBackAsync(PutBackCommand, message, cancellationToken);

    private const string CreatedAt = $"[{nameof(OutboxMessage.CreatedAt)}]";
    private const string FirstReturnedAt = $"[{nameof(OutboxMessage.FirstReturnedAt)}]";

    // one short transaction: the row is looked up by key and waited for when a relay holds it. The lock on the key,
    // or on where it would be, is kept until the row is updated or inserted, so that nobody stores the same message
    // in between. A failure ends the transaction
    private const string PutBackCommand =
        "SET XACT_ABORT ON; BEGIN TRANSACTION; " +
        $"UPDATE {Table} WITH (UPDLOCK, HOLDLOCK) SET {DeliverAt} = {{6}}, " +
        $"{FirstReturnedAt} = CASE WHEN {FirstReturnedAt} >= {CreatedAt} AND {FirstReturnedAt} < {{7}} THEN {FirstReturnedAt} ELSE {{7}} END " +
        $"WHERE {MessageId} = {{0}}; " +
        $"IF @@ROWCOUNT = 0 INSERT INTO {Table} ({MessageId}, [{nameof(OutboxMessage.CorrelationId)}], [{nameof(OutboxMessage.Body)}], " +
        $"[{nameof(OutboxMessage.MessageType)}], {CreatedAt}, [{nameof(OutboxMessage.SenderId)}], {DeliverAt}, {FirstReturnedAt}) " +
        "VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}); " +
        "COMMIT TRANSACTION; SET XACT_ABORT OFF;";

    // SQL Server is not given one statement listing the keys: depending on the version it reads the list as JSON,
    // which needs compatibility level 130, and it may scan the table for it, waiting on rows another relay holds.
    // The rows are deleted by key instead, one statement each, all sent together
    protected override async ValueTask DeleteBatchAsync(IReadOnlyCollection<MessageEnvelope> messages, CancellationToken cancellationToken)
    {
        var dbContext = RelayDbContext;
        var rows = messages.DistinctBy(message => message.MessageId).Select(ToKeyOnlyRow).ToArray();

        try
        {
            dbContext.OutboxMessages.AttachRange(rows);
            dbContext.OutboxMessages.RemoveRange(rows);

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // one of the rows was already gone: the others are deleted one by one, which does not mind
            StopTracking(dbContext, rows);
            foreach (var message in messages)
                await DeleteAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            StopTracking(dbContext, rows);
            throw;
        }
    }

    private static void StopTracking(SagaDbContext dbContext, IEnumerable<OutboxMessage> rows)
    {
        foreach (var row in rows)
            dbContext.Entry(row).State = EntityState.Detached;
    }

    private static OutboxMessage ToKeyOnlyRow(MessageEnvelope message)
        => new()
        {
            MessageId = message.MessageId,
            CorrelationId = message.CorrelationId,
            SenderId = message.SenderId,
            MessageType = string.Empty,
            Body = [],
            CreatedAt = message.CreatedAt,
        };
}