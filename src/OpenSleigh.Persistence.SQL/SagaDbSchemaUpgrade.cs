using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OpenSleigh.Persistence.SQL;

/// <summary>
/// brings the schema of a database created by an earlier version of the library up to the current one.
/// Every change is checked with a query first and only applied when it is missing,
/// so a database that is up to date, or was upgraded by hand, needs no permission to change it.
/// </summary>
public abstract class SagaDbSchemaUpgrade
{
    /// <summary>
    /// the name of the column the queries of an upgrade return.
    /// </summary>
    public const string ResultColumn = "Value";

    private const int Yes = 1;

    /// <summary>
    /// how long a statement of the upgrade can wait for a lock on a table before it gives up.
    /// Short: while it waits, everything else that uses the table waits behind it.
    /// </summary>
    protected static readonly TimeSpan TableLockTimeout = TimeSpan.FromSeconds(5);

    private readonly ILogger _logger;

    protected SagaDbSchemaUpgrade(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// the changes, in the order they are applied.
    /// </summary>
    public abstract IReadOnlyList<SagaDbSchemaChange> Changes { get; }

    /// <summary>
    /// a query returning one row with one integer column named <see cref="ResultColumn"/>:
    /// 1 when the tables the changes apply to exist.
    /// </summary>
    protected abstract string TablesExistQuery { get; }

    /// <summary>
    /// how long a statement of the upgrade can run: building an index on a large table takes longer than a query does.
    /// It is also how long an instance waits for another one that is applying the same change.
    /// </summary>
    protected virtual TimeSpan CommandTimeout => TimeSpan.FromMinutes(10);

    /// <summary>
    /// runs <paramref name="apply"/>, which applies a change if it is still missing, while no other instance
    /// is upgrading the same database: instances that start together wait for each other here,
    /// and all but the first find the change applied.
    /// </summary>
    protected abstract void RunExclusively(DatabaseFacade database, SagaDbSchemaChange change, Action apply);

    /// <summary>
    /// whether the database refused a statement because the user is not allowed to run it.
    /// </summary>
    protected abstract bool IsPermissionDenied(Exception exception);

    public virtual void Apply(DatabaseFacade database)
    {
        ArgumentNullException.ThrowIfNull(database);

        var missing = Changes.Where(change => !IsApplied(database, change)).ToArray();
        if (missing.Length == 0)
            return;

        if (!IsTrue(database, TablesExistQuery))
            throw new InvalidOperationException(
                "The saga database has tables, but not the ones of OpenSleigh, which are only created in a database that has none. " +
                "Either another instance is creating them right now, and starting again is enough, " +
                "or the database is used for something else: create the tables of OpenSleigh in it by hand, or use an empty database.");

        var previousTimeout = database.GetCommandTimeout();
        database.SetCommandTimeout(CommandTimeout);
        try
        {
            foreach (var change in missing)
                ApplyAndReport(database, change);
        }
        finally
        {
            database.SetCommandTimeout(previousTimeout);
        }
    }

    private void ApplyAndReport(DatabaseFacade database, SagaDbSchemaChange change)
    {
        _logger.LogInformation(
            "Upgrading the saga database schema: {Change}. Waiting for other instances doing the same...",
            change.Description);

        try
        {
            RunExclusively(database, change, () => ApplyIfStillMissing(database, change));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The saga database schema could not be upgraded: {Change}.", change.Description);
            throw new InvalidOperationException(DescribeFailure(change, ex), ex);
        }

        _logger.LogInformation("Saga database schema upgraded: {Change}.", change.Description);
    }

    private string DescribeFailure(SagaDbSchemaChange change, Exception exception)
    {
        var statements = string.Join("; ", change.Statements);
        var failure = $"The saga database schema could not be upgraded ({change.Description}). ";

        return IsPermissionDenied(exception)
            ? failure + "The database user is not allowed to change the schema. " +
              $"Apply the change with a user that is, then start the application again: {statements}"
            : failure + $"{exception.Message} " +
              $"The upgrade is tried again the next time the application starts. It can also be applied by hand: {statements}";
    }

    // it was missing before the wait for the other instances: one of them may have applied it since,
    // and applying it again would need the table, and the permission to change it, for nothing
    private static void ApplyIfStillMissing(DatabaseFacade database, SagaDbSchemaChange change)
    {
        if (IsApplied(database, change))
            return;

        foreach (var statement in change.Statements)
            database.ExecuteSqlRaw(statement);
    }

    private static bool IsApplied(DatabaseFacade database, SagaDbSchemaChange change)
        => IsTrue(database, change.IsAppliedQuery);

    private static bool IsTrue(DatabaseFacade database, string query)
        => database.SqlQueryRaw<int>(query).AsEnumerable().Single() == Yes;
}
