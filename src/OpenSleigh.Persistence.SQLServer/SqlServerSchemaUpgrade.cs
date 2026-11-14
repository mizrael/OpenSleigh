using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Persistence.SQL.Entities;

namespace OpenSleigh.Persistence.SQLServer;

/// <summary>
/// brings a database created by an earlier version up to date: the outbox due time and its index, for delayed delivery,
/// and the table of the messages whose handler failed.
/// The first two require the ALTER permission on the outbox table, the last one CREATE TABLE in the database
/// and ALTER on the schema.
/// </summary>
/// <remarks>
/// The column is nullable and has no default, so adding it does not rewrite the table on any edition.
/// The index is built online where the edition can, which does not stop the outbox from being read and written
/// while it lasts; on the others the outbox waits for the build.
/// </remarks>
internal sealed class SqlServerSchemaUpgrade : SagaDbSchemaUpgrade
{
    private const string OutboxTable = $"[{Constants.DbSchema}].[{OutboxMessage.TableName}]";
    private const string FailuresTable = $"[{Constants.DbSchema}].[{SagaFailureRecord.TableName}]";
    private const string HasFailuresTable = $"OBJECT_ID(N'{FailuresTable}', N'U') IS NOT NULL";
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
    private static readonly int[] PermissionDeniedErrors = [229, 230, 262, 297, 300, 1088, 2760, 3704, 4902, 15247];

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

        // one transaction: the table is there with its indexes, or not at all. It is new, so nothing waits for it
        new SagaDbSchemaChange(
            "the table of the messages whose handler failed",
            $"SELECT CASE WHEN {HasFailuresTable} THEN 1 ELSE 0 END AS [{ResultColumn}]",
            [
                $"IF NOT ({HasFailuresTable}) BEGIN " +
                $"CREATE TABLE {FailuresTable} (" +
                $"[{nameof(SagaFailureRecord.Id)}] nvarchar(450) NOT NULL, " +
                $"[{nameof(SagaFailureRecord.InstanceId)}] nvarchar(450) NOT NULL, " +
                $"[{nameof(SagaFailureRecord.CorrelationId)}] nvarchar(450) NOT NULL, " +
                $"[{nameof(SagaFailureRecord.SagaType)}] nvarchar(max) NOT NULL, " +
                $"[{nameof(SagaFailureRecord.MessageId)}] nvarchar(450) NOT NULL, " +
                $"[{nameof(SagaFailureRecord.MessageType)}] nvarchar(max) NOT NULL, " +
                $"[{nameof(SagaFailureRecord.Attempt)}] int NOT NULL, " +
                $"[{nameof(SagaFailureRecord.OccurredAt)}] datetimeoffset NOT NULL, " +
                $"[{nameof(SagaFailureRecord.ExceptionType)}] nvarchar(max) NOT NULL, " +
                $"[{nameof(SagaFailureRecord.ExceptionMessage)}] nvarchar(max) NOT NULL, " +
                $"[{nameof(SagaFailureRecord.StackTrace)}] nvarchar(max) NULL, " +
                $"[{nameof(SagaFailureRecord.Decision)}] nvarchar(max) NOT NULL, " +
                $"[{nameof(SagaFailureRecord.RetryAt)}] datetimeoffset NULL, " +
                $"[{nameof(SagaFailureRecord.CompensationError)}] nvarchar(max) NULL, " +
                $"CONSTRAINT [PK_{SagaFailureRecord.TableName}] PRIMARY KEY ([{nameof(SagaFailureRecord.Id)}])); " +
                $"CREATE INDEX [{SagaFailureRecord.MessageIndexName}] ON {FailuresTable} ([{nameof(SagaFailureRecord.InstanceId)}], [{nameof(SagaFailureRecord.MessageId)}]); " +
                $"CREATE INDEX [{SagaFailureRecord.CorrelationIndexName}] ON {FailuresTable} ([{nameof(SagaFailureRecord.CorrelationId)}]); " +
                $"CREATE INDEX [{SagaFailureRecord.OccurredAtIndexName}] ON {FailuresTable} ([{nameof(SagaFailureRecord.OccurredAt)}]); " +
                "END",
            ]),
    ];

    private readonly string _lockStatement;
    private readonly string _waitForTableLocks;

    public SqlServerSchemaUpgrade(ILogger<SagaDbSchemaUpgrade>? logger = null) : base(logger)
    {
        // sp_getapplock reports a lock it did not get (timeout, cancellation, deadlock) with a negative return value,
        // not with an error: the statements that follow would run without the lock
        _lockStatement =
            "DECLARE @result int; " +
            $"EXEC @result = sp_getapplock @Resource = N'{ConcurrentUpgradesLockName}', @LockMode = N'Exclusive', " +
            $"@LockOwner = N'Transaction', @LockTimeout = {(int)CommandTimeout.TotalMilliseconds}; " +
            "IF @result < 0 BEGIN; " +
            "THROW 51000, N'the lock that makes schema upgrades wait for each other was not granted.', 1; " +
            "END";

        _waitForTableLocks = $"SET LOCK_TIMEOUT {(int)TableLockTimeout.TotalMilliseconds}";
    }

    public override IReadOnlyList<SagaDbSchemaChange> Changes => SchemaChanges;

    protected override string TablesExistQuery
        => $"SELECT CASE WHEN OBJECT_ID(N'{OutboxTable}', N'U') IS NOT NULL THEN 1 ELSE 0 END AS [{ResultColumn}]";

    protected override void RunExclusively(DatabaseFacade database, SagaDbSchemaChange change, Action apply)
    {
        using var transaction = database.BeginTransaction();
        try
        {
            // held until the transaction ends
            database.ExecuteSqlRaw(_lockStatement);

            database.ExecuteSqlRaw(_waitForTableLocks);
            apply();

            transaction.Commit();
        }
        finally
        {
            TryStopWaitingForTableLocks(database);
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
