namespace OpenSleigh.Persistence.SQL;

/// <summary>
/// one change to the schema of a database created by an earlier version of the library.
/// </summary>
/// <param name="Description">what the change adds, for logs and errors.</param>
/// <param name="IsAppliedQuery">
/// a query returning one row with one integer column named <see cref="SagaDbSchemaUpgrade.ResultColumn"/>:
/// 1 when the database already has what the change adds.
/// </param>
/// <param name="Statements">
/// the statements that apply the change, in order. They are only run when <paramref name="IsAppliedQuery"/> says
/// the change is missing, and can also be applied by hand.
/// </param>
/// <param name="RunsInTransaction">false for statements the database refuses to run inside a transaction.</param>
public sealed record SagaDbSchemaChange(
    string Description,
    string IsAppliedQuery,
    IReadOnlyList<string> Statements,
    bool RunsInTransaction = true);
