using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal;
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
    private const string FirstReturnedAtColumn = nameof(OutboxMessage.FirstReturnedAt);
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

        new SagaDbSchemaChange(
            "the time outbox messages were first returned by the transport",
            $"""
             SELECT CASE WHEN EXISTS (
                 SELECT 1 FROM information_schema.columns
                 WHERE table_schema = '{Constants.DbSchema}' AND table_name = '{OutboxMessage.TableName}' AND column_name = '{FirstReturnedAtColumn}')
             THEN 1 ELSE 0 END AS "{ResultColumn}"
             """,
            [$"ALTER TABLE {OutboxTable} ADD COLUMN IF NOT EXISTS \"{FirstReturnedAtColumn}\" timestamp with time zone"]),
    ];

    public PostgreSqlSchemaUpgrade(ILogger<SagaDbSchemaUpgrade>? logger = null) : base(logger)
    {
    }

    public override IReadOnlyList<SagaDbSchemaChange> Changes => SchemaChanges;

    protected override string TablesExistQuery
        => $"SELECT CASE WHEN to_regclass('{OutboxTable}') IS NOT NULL THEN 1 ELSE 0 END AS \"{ResultColumn}\"";

    // the lock belongs to the connection, not to a transaction, because the index is built outside one
    // and the tables are created in a transaction of their own: the connection is kept for all the statements
    protected override void RunExclusively(DatabaseFacade database, SagaDbSchemaChange change, Action apply)
        => RunExclusively(database, change.RunsInTransaction ? () => RunInTransaction(database, apply) : apply);

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
                ReleaseLockOrConnection(database);
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
                    $"another instance has been creating, upgrading or seeding the schema for more than {CommandTimeout.TotalMinutes} minutes.");

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

    // a connection goes back to its pool as it is, and is only reset when it is taken out again:
    // one that still has the lock would keep every other instance waiting until then, so it is not kept
    internal static void ReleaseLockOrConnection(DatabaseFacade database)
    {
        try
        {
            database.ExecuteSqlRaw(ReleaseLockForConnection);
        }
        catch
        {
            ClearConnectionPool(database);
        }
    }

    // the connections of a data source are pooled by the data source, not by their connection string
    private static void ClearConnectionPool(DatabaseFacade database)
    {
#pragma warning disable EF1001 // the provider has no public way to the data source of its connection
        var dataSource = database.GetService<INpgsqlRelationalConnection>().DataSource;
#pragma warning restore EF1001

        if (dataSource is NpgsqlDataSource npgsqlDataSource)
            npgsqlDataSource.Clear();
        else if (database.GetDbConnection() is NpgsqlConnection connection)
            NpgsqlConnection.ClearPool(connection);
    }

    // what PostgreSQL copies a new database from, unless the model names another one
    private const string DefaultTemplateDatabase = "template1";

    // another arbitrary number: creating a database does not wait for the upgrade of another one
    private const string ConcurrentCreationsLockKey = "7308325172346594412";

    // the server has a database before any other: instances that find theirs missing wait for each other there,
    // on the connection the provider creates the database with, which signs in the way the application configured.
    // The lock belongs to that connection, which is not pooled: closing it lets go of the lock
    protected override void RunDatabaseCreationExclusively(DatabaseFacade database, Action createDatabase)
    {
#pragma warning disable EF1001 // the provider has no public way to its connection to the administration database
        using var admin = database.GetService<INpgsqlRelationalConnection>().CreateAdminConnection();
#pragma warning restore EF1001
        ThrowIfTheTemplate(database, admin.DbConnection.Database);

        admin.Open();
        try
        {
            using (var command = admin.DbConnection.CreateCommand())
            {
                command.CommandTimeout = (int)CommandTimeout.TotalSeconds;
                command.CommandText = $"SELECT pg_advisory_lock({ConcurrentCreationsLockKey})";
                command.ExecuteNonQuery();
            }

            createDatabase();
        }
        finally
        {
            // disposing it does not close a connection the provider cloned from a data source
            admin.Close();
        }
    }

    // PostgreSQL does not copy a database another session is connected to, and the one that has the lock
    // stays connected to the administration database while the saga database is created
    private static void ThrowIfTheTemplate(DatabaseFacade database, string adminDatabase)
    {
        var template = database.GetService<IDesignTimeModel>().Model.GetDatabaseTemplate() ?? DefaultTemplateDatabase;

        if (string.Equals(adminDatabase, template, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"The saga database is missing, and cannot be created: '{adminDatabase}' is both the administration database " +
                "and the template a new database is copied from, and PostgreSQL does not copy a database that is in use. " +
                $"Configure another administration database ('{nameof(Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder.UseAdminDatabase)}'), " +
                "or create the saga database before the application starts.");
    }

    protected override bool IsPermissionDenied(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException { SqlState: PermissionDeniedSqlState })
                return true;

        return false;
    }
}
