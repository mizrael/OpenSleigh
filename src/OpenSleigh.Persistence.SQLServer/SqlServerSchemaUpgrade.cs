using OpenSleigh.Persistence.SQL;
using OpenSleigh.Persistence.SQL.Entities;

namespace OpenSleigh.Persistence.SQLServer;

/// <summary>
/// adds the outbox due time, and its index, to a database created before delayed delivery existed.
/// Both statements require the ALTER permission on the table.
/// </summary>
internal sealed class SqlServerSchemaUpgrade : SagaDbSchemaUpgrade
{
    private const string OutboxTable = $"[{Constants.DbSchema}].[{OutboxMessage.TableName}]";
    private const string DeliverAtColumn = nameof(OutboxMessage.DeliverAt);
    private const string ConcurrentUpgradesLockName = "OpenSleigh.SchemaUpgrade";

    private const string HasDeliverAtColumn = $"COL_LENGTH(N'{OutboxTable}', N'{DeliverAtColumn}') IS NOT NULL";

    private const string HasDeliverAtIndex =
        $"EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'{OutboxTable}') AND name = N'{OutboxMessage.DeliverAtIndexName}')";

    private static readonly IReadOnlyList<string> UpgradeStatements =
    [
        $"IF NOT ({HasDeliverAtColumn}) ALTER TABLE {OutboxTable} ADD [{DeliverAtColumn}] datetimeoffset NOT NULL DEFAULT '1970-01-01T00:00:00.0000000+00:00'",
        $"IF NOT {HasDeliverAtIndex} CREATE INDEX [{OutboxMessage.DeliverAtIndexName}] ON {OutboxTable} ([{DeliverAtColumn}])",
    ];

    protected override string IsUpToDateQuery
        => $"SELECT CASE WHEN {HasDeliverAtColumn} AND {HasDeliverAtIndex} THEN 1 ELSE 0 END AS [{UpToDateColumn}]";

    protected override string ConcurrentUpgradesLockStatement
        => $"EXEC sp_getapplock @Resource = N'{ConcurrentUpgradesLockName}', @LockMode = N'Exclusive', @LockOwner = N'Transaction'";

    public override IReadOnlyList<string> Statements => UpgradeStatements;
}
