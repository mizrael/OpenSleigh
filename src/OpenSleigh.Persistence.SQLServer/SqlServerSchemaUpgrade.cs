using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.SqlServer.Storage.Internal;
using Microsoft.Extensions.Logging;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Persistence.SQL.Entities;

namespace OpenSleigh.Persistence.SQLServer;

/// <summary>
/// adds the outbox due time, and its index, to a database created before delayed delivery existed.
/// Both require the ALTER permission on the outbox table.
/// </summary>
/// <remarks>
/// The column is nullable and has no default, so adding it does not rewrite the table on any edition.
/// The index is built online where the edition can, which does not stop the outbox from being read and written
/// while it lasts; on the others the outbox waits for the build.
/// </remarks>
internal sealed class SqlServerSchemaUpgrade : SagaDbSchemaUpgrade
{
    private const string OutboxTable = $"[{Constants.DbSchema}].[{OutboxMessage.TableName}]";
    private const string DeliverAtColumn = nameof(OutboxMessage.DeliverAt);
    private const string DeliverAtIndex = $"[{OutboxMessage.DeliverAtIndexName}]";
    internal const string ConcurrentUpgradesLockName = "OpenSleigh.SchemaUpgrade";

    private const string HasDeliverAtColumn = $"COL_LENGTH(N'{OutboxTable}', N'{DeliverAtColumn}') IS NOT NULL";

    private const string HasDeliverAtIndex =
        $"EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'{OutboxTable}') AND name = N'{OutboxMessage.DeliverAtIndexName}')";

    private const string CreateDeliverAtIndex = $"CREATE INDEX {DeliverAtIndex} ON {OutboxTable} ([{DeliverAtColumn}])";

    // Enterprise (and Developer), Azure SQL Database and Azure SQL Managed Instance
    private const string CanBuildIndexesOnline = "CAST(SERVERPROPERTY('EngineEdition') AS int) IN (3, 5, 8)";

    private const string StopWaitingForTableLocks = "SET LOCK_TIMEOUT -1";

    // a permission that is missing, or an object the user is not allowed to see
    private static readonly int[] PermissionDeniedErrors = [229, 230, 262, 297, 300, 1088, 3704, 4902, 15247];

    private static readonly IReadOnlyList<SagaDbSchemaChange> SchemaChanges =
    [
        new SagaDbSchemaChange(
            "the due time of outbox messages",
            $"SELECT CASE WHEN {HasDeliverAtColumn} THEN 1 ELSE 0 END AS [{ResultColumn}]",
            [$"IF NOT ({HasDeliverAtColumn}) ALTER TABLE {OutboxTable} ADD [{DeliverAtColumn}] datetimeoffset NULL"]),

        new SagaDbSchemaChange(
            "the index on the due time of outbox messages",
            $"SELECT CASE WHEN {HasDeliverAtIndex} THEN 1 ELSE 0 END AS [{ResultColumn}]",
            [
                $"IF NOT {HasDeliverAtIndex} BEGIN " +
                $"IF {CanBuildIndexesOnline} EXEC(N'{CreateDeliverAtIndex} WITH (ONLINE = ON)') " +
                $"ELSE EXEC(N'{CreateDeliverAtIndex}') " +
                "END",
            ]),
    ];

    private readonly string _lockStatement;
    private readonly string _waitForTableLocks;

    // released when asked to, or when the connection is closed for good
    private const string ReleaseLock =
        $"EXEC sp_releaseapplock @Resource = N'{ConcurrentUpgradesLockName}', @LockOwner = N'Session'";

    public SqlServerSchemaUpgrade(ILogger<SagaDbSchemaUpgrade>? logger = null) : base(logger)
    {
        // sp_getapplock reports a lock it did not get (timeout, cancellation, deadlock) with a negative return value,
        // not with an error: the statements that follow would run without the lock
        _lockStatement =
            "DECLARE @result int; " +
            $"EXEC @result = sp_getapplock @Resource = N'{ConcurrentUpgradesLockName}', @LockMode = N'Exclusive', " +
            $"@LockOwner = N'Session', @LockTimeout = {(int)CommandTimeout.TotalMilliseconds}; " +
            "IF @result < 0 BEGIN; " +
            "THROW 51000, N'the lock that makes instances wait for each other to create, upgrade or seed the schema was not granted.', 1; " +
            "END";

        _waitForTableLocks = $"SET LOCK_TIMEOUT {(int)TableLockTimeout.TotalMilliseconds}";
    }

    public override IReadOnlyList<SagaDbSchemaChange> Changes => SchemaChanges;

    protected override string TablesExistQuery
        => $"SELECT CASE WHEN OBJECT_ID(N'{OutboxTable}', N'U') IS NOT NULL THEN 1 ELSE 0 END AS [{ResultColumn}]";

    protected override void RunExclusively(DatabaseFacade database, SagaDbSchemaChange change, Action apply)
        => RunExclusively(database, () => RunInTransaction(database, apply));

    private void RunInTransaction(DatabaseFacade database, Action apply)
    {
        using var transaction = database.BeginTransaction();
        try
        {
            database.ExecuteSqlRaw(_waitForTableLocks);
            apply();

            transaction.Commit();
        }
        finally
        {
            TryStopWaitingForTableLocks(database);
        }
    }

    // the lock belongs to the connection, not to a transaction, because the tables are created in a transaction
    // of their own: the connection is kept for all the statements
    protected override void RunExclusively(DatabaseFacade database, Action action)
    {
        database.OpenConnection();
        try
        {
            database.ExecuteSqlRaw(_lockStatement);
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

    // a connection goes back to its pool as it is, and is only reset when it is taken out again:
    // one that still has the lock would keep every other instance waiting until then, so it is not kept
    private static void ReleaseLockOrConnection(DatabaseFacade database)
    {
        try
        {
            database.ExecuteSqlRaw(ReleaseLock);
        }
        catch
        {
            ClearConnectionPool(database);
        }
    }

    private static void ClearConnectionPool(DatabaseFacade database)
    {
        if (database.GetDbConnection() is SqlConnection connection)
            SqlConnection.ClearPool(connection);
    }

    private const string CreationLockPrefix = "OpenSleigh.CreateDatabase.";
    private const int MaxLockNameLength = 255;

    // the server has a database before any other: instances that find theirs missing wait for each other there,
    // on the connection the provider creates the database with, which signs in the way the application configured.
    // The lock belongs to that connection, which is not pooled: closing it lets go of the lock
    protected override void RunDatabaseCreationExclusively(DatabaseFacade database, Action createDatabase)
    {
        var lockName = CreationLockPrefix + new SqlConnectionStringBuilder(database.GetConnectionString()).InitialCatalog;

#pragma warning disable EF1001 // the provider has no public way to its connection to the system database
        using var master = database.GetService<ISqlServerConnection>().CreateMasterConnection();
#pragma warning restore EF1001
        master.ConnectionString = new SqlConnectionStringBuilder(master.ConnectionString) { Pooling = false }.ConnectionString;
        master.Open();
        try
        {
            using (var command = (SqlCommand)master.DbConnection.CreateCommand())
            {
                command.CommandTimeout = (int)CommandTimeout.TotalSeconds;
                command.CommandText =
                    "DECLARE @result int; " +
                    "EXEC @result = sp_getapplock @Resource = @name, @LockMode = N'Exclusive', " +
                    $"@LockOwner = N'Session', @LockTimeout = {(int)CommandTimeout.TotalMilliseconds}; " +
                    "IF @result < 0 BEGIN; " +
                    "THROW 51000, N'the lock that makes instances wait for each other to create the database was not granted.', 1; " +
                    "END";
                command.Parameters.AddWithValue("@name", lockName.Length > MaxLockNameLength ? lockName[..MaxLockNameLength] : lockName);
                command.ExecuteNonQuery();
            }

            // the login that failed because the database was missing is remembered by the pool for a while,
            // and answered with again without asking the server: the database another instance created would not be seen
            ClearConnectionPool(database);

            createDatabase();
        }
        finally
        {
            master.Close();
        }
    }

    // the setting belongs to the connection, which goes back to its pool
    private static void TryStopWaitingForTableLocks(DatabaseFacade database)
    {
        try
        {
            database.ExecuteSqlRaw(StopWaitingForTableLocks);
        }
        catch
        {
            // the connection is gone, and its settings with it: the failure that broke it is the one to report
        }
    }

    protected override bool IsPermissionDenied(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is SqlException sqlException && PermissionDeniedErrors.Contains(sqlException.Number))
                return true;

        return false;
    }
}
