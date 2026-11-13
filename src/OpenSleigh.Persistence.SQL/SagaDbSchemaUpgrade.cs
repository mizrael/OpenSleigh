using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace OpenSleigh.Persistence.SQL;

/// <summary>
/// brings the schema of a database created by an earlier version of the library up to the current one.
/// The schema is checked with one query; the statements only run when something is missing,
/// so a database that is up to date, or was upgraded by hand, needs no permission to change it.
/// </summary>
public abstract class SagaDbSchemaUpgrade
{
    /// <summary>
    /// the name of the column <see cref="IsUpToDateQuery"/> returns.
    /// </summary>
    protected const string UpToDateColumn = "Value";

    private const int UpToDate = 1;

    /// <summary>
    /// a query returning one row with one integer column named <see cref="UpToDateColumn"/>: 1 when nothing is missing.
    /// </summary>
    protected abstract string IsUpToDateQuery { get; }

    /// <summary>
    /// a statement that makes concurrent upgrades of the same database wait for each other, until the transaction ends.
    /// </summary>
    protected abstract string ConcurrentUpgradesLockStatement { get; }

    /// <summary>
    /// the statements that add what is missing. Each of them does nothing when there is nothing to add.
    /// </summary>
    public abstract IReadOnlyList<string> Statements { get; }

    public virtual void Apply(DatabaseFacade database)
    {
        ArgumentNullException.ThrowIfNull(database);

        if (IsUpToDate(database))
            return;

        try
        {
            using var transaction = database.BeginTransaction();

            database.ExecuteSqlRaw(ConcurrentUpgradesLockStatement);
            foreach (var statement in Statements)
                database.ExecuteSqlRaw(statement);

            transaction.Commit();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "The saga database was created by an earlier version of OpenSleigh and could not be upgraded. " +
                "The database user needs the permission to alter the outbox table and to create an index on it. " +
                "Otherwise, apply these statements with a user that has it and start the application again: " +
                string.Join("; ", Statements),
                ex);
        }
    }

    private bool IsUpToDate(DatabaseFacade database)
        => database.SqlQueryRaw<int>(IsUpToDateQuery).AsEnumerable().Single() == UpToDate;
}
