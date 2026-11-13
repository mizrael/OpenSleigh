using OpenSleigh.Persistence.SQL;
using OpenSleigh.Persistence.SQL.Entities;

namespace OpenSleigh.Persistence.PostgreSQL;

/// <summary>
/// adds the outbox due time, and its index, to a database created before delayed delivery existed.
/// Altering the table and creating the index require the user to own the table.
/// </summary>
internal sealed class PostgreSqlSchemaUpgrade : SagaDbSchemaUpgrade
{
    private const string OutboxTable = $"{Constants.DbSchema}.\"{OutboxMessage.TableName}\"";
    private const string DeliverAtColumn = nameof(OutboxMessage.DeliverAt);

    // an arbitrary number: all it has to be is the same for every instance of the library
    private const long ConcurrentUpgradesLockKey = 7308325172346594411;

    private static readonly IReadOnlyList<string> UpgradeStatements =
    [
        $"ALTER TABLE {OutboxTable} ADD COLUMN IF NOT EXISTS \"{DeliverAtColumn}\" timestamp with time zone NOT NULL DEFAULT TIMESTAMPTZ '1970-01-01T00:00:00+00:00'",
        $"CREATE INDEX IF NOT EXISTS \"{OutboxMessage.DeliverAtIndexName}\" ON {OutboxTable} (\"{DeliverAtColumn}\")",
    ];

    protected override string IsUpToDateQuery =>
        $"""
        SELECT CASE WHEN
            EXISTS (SELECT 1 FROM information_schema.columns
                    WHERE table_schema = '{Constants.DbSchema}' AND table_name = '{OutboxMessage.TableName}' AND column_name = '{DeliverAtColumn}')
            AND EXISTS (SELECT 1 FROM pg_indexes
                    WHERE schemaname = '{Constants.DbSchema}' AND tablename = '{OutboxMessage.TableName}' AND indexname = '{OutboxMessage.DeliverAtIndexName}')
        THEN 1 ELSE 0 END AS "{UpToDateColumn}"
        """;

    protected override string ConcurrentUpgradesLockStatement
        => $"SELECT pg_advisory_xact_lock({ConcurrentUpgradesLockKey})";

    public override IReadOnlyList<string> Statements => UpgradeStatements;
}
