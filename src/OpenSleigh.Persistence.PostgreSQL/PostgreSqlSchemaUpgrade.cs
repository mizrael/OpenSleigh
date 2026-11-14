using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Persistence.SQL.Entities;

namespace OpenSleigh.Persistence.PostgreSQL;

/// <summary>
/// adds the outbox due time, and its index, to a database created before delayed delivery existed.
/// Both require the user to own the outbox table.
/// </summary>
/// <remarks>
/// The column has no default, so adding it does not rewrite the table: it needs the table for a moment only.
/// The index is built concurrently, which does not stop the outbox from being read and written while it lasts.
/// </remarks>
internal sealed class PostgreSqlSchemaUpgrade : SagaDbSchemaUpgrade
{
    private const string OutboxTable = $"{Constants.DbSchema}.\"{OutboxMessage.TableName}\"";
    private const string DeliverAtColumn = nameof(OutboxMessage.DeliverAt);
    private const string DeliverAtIndex = $"{Constants.DbSchema}.\"{OutboxMessage.DeliverAtIndexName}\"";
    private const string PermissionDeniedSqlState = "42501";

    // an arbitrary number: all it has to be is the same for every instance of the library
    internal const string ConcurrentUpgradesLockKey = "7308325172346594411";

    // held until it is released, or the connection is closed
    internal const string LockForConnection = $"SELECT pg_advisory_lock({ConcurrentUpgradesLockKey})";
    internal const string ReleaseLockForConnection = $"SELECT pg_advisory_unlock({ConcurrentUpgradesLockKey})";

    private const string TryLockForConnection =
        $"SELECT CASE WHEN pg_try_advisory_lock({ConcurrentUpgradesLockKey}) THEN 1 ELSE 0 END AS \"{ResultColumn}\"";

    private const int LockGranted = 1;
    private static readonly TimeSpan LockRetryInterval = TimeSpan.FromMilliseconds(250);

    private static readonly string SetTableLockTimeout =
        $"SET LOCAL lock_timeout = '{(int)TableLockTimeout.TotalMilliseconds}ms'";

    private static readonly IReadOnlyList<SagaDbSchemaChange> SchemaChanges =
    [
        new SagaDbSchemaChange(
            "the due time of outbox messages",
            $"""
             SELECT CASE WHEN EXISTS (
                 SELECT 1 FROM information_schema.columns
                 WHERE table_schema = '{Constants.DbSchema}' AND table_name = '{OutboxMessage.TableName}' AND column_name = '{DeliverAtColumn}')
             THEN 1 ELSE 0 END AS "{ResultColumn}"
             """,
            [$"ALTER TABLE {OutboxTable} ADD COLUMN IF NOT EXISTS \"{DeliverAtColumn}\" timestamp with time zone"]),

        // a concurrent build that failed leaves an index behind that is not valid, and is never used: it is built again
        new SagaDbSchemaChange(
            "the index on the due time of outbox messages",
            $"""
             SELECT CASE WHEN EXISTS (
                 SELECT 1 FROM pg_index
                 JOIN pg_class ON pg_class.oid = pg_index.indexrelid
                 JOIN pg_namespace ON pg_namespace.oid = pg_class.relnamespace
                 WHERE pg_namespace.nspname = '{Constants.DbSchema}' AND pg_class.relname = '{OutboxMessage.DeliverAtIndexName}' AND pg_index.indisvalid)
             THEN 1 ELSE 0 END AS "{ResultColumn}"
             """,
            [
                $"DROP INDEX CONCURRENTLY IF EXISTS {DeliverAtIndex}",
                $"CREATE INDEX CONCURRENTLY IF NOT EXISTS \"{OutboxMessage.DeliverAtIndexName}\" ON {OutboxTable} (\"{DeliverAtColumn}\")",
            ],
            RunsInTransaction: false),
    ];

    public PostgreSqlSchemaUpgrade(ILogger<SagaDbSchemaUpgrade>? logger = null) : base(logger)
    {
    }

    public override IReadOnlyList<SagaDbSchemaChange> Changes => SchemaChanges;

    protected override string TablesExistQuery
        => $"SELECT CASE WHEN to_regclass('{OutboxTable}') IS NOT NULL THEN 1 ELSE 0 END AS \"{ResultColumn}\"";

    // the lock belongs to the connection, not to a transaction, because the index is built outside one:
    // the connection is kept for all the statements
    protected override void RunExclusively(DatabaseFacade database, SagaDbSchemaChange change, Action apply)
        => RunExclusively(database, change.RunsInTransaction ? () => RunInTransaction(database, apply) : apply);

    // the same lock for creating the tables: they are created in a transaction of their own
    protected override void RunExclusively(DatabaseFacade database, Action action)
    {
        database.OpenConnection();
        try
        {
            WaitForLock(database);
            try
            {
                action();
            }
            finally
            {
                TryReleaseLock(database);
            }
        }
        finally
        {
            database.CloseConnection();
        }
    }

    // asked for again and again instead of waited for: a statement that waits keeps its view of the database open
    // for as long as it does, and an index that is being built concurrently, by the instance that has the lock,
    // waits for every such view to close. The two would wait for each other
    private void WaitForLock(DatabaseFacade database)
    {
        var deadline = DateTimeOffset.UtcNow + CommandTimeout;

        while (database.SqlQueryRaw<int>(TryLockForConnection).AsEnumerable().Single() != LockGranted)
        {
            if (DateTimeOffset.UtcNow > deadline)
                throw new TimeoutException(
                    $"another instance has been upgrading the schema for more than {CommandTimeout.TotalMinutes} minutes.");

            Thread.Sleep(LockRetryInterval);
        }
    }

    private static void RunInTransaction(DatabaseFacade database, Action apply)
    {
        using var transaction = database.BeginTransaction();

        database.ExecuteSqlRaw(SetTableLockTimeout);
        apply();

        transaction.Commit();
    }

    private static void TryReleaseLock(DatabaseFacade database)
    {
        try
        {
            database.ExecuteSqlRaw(ReleaseLockForConnection);
        }
        catch
        {
            // the connection is gone, and its lock with it: the failure that broke it is the one to report
        }
    }

    protected override bool IsPermissionDenied(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException { SqlState: PermissionDeniedSqlState })
                return true;

        return false;
    }
}
