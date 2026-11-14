using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenSleigh.Persistence.SQL.Tests.Unit;

/// <summary>
/// what every schema upgrade does around its changes, on a SQLite file: no server, so these run everywhere.
/// What the providers add (locks, timeouts, their statements) is checked against PostgreSQL and SQL Server
/// by the integration tests.
/// </summary>
public sealed class SagaDbSchemaUpgradeTests : IDisposable
{
    private const string Table = "Outbox";
    private const string NewColumn = "DueTime";
    private const string AddColumn = $"ALTER TABLE {Table} ADD COLUMN {NewColumn} TEXT";

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"osl_upgrade_{Guid.NewGuid():N}.db");
    private readonly DbContext _context;

    public SagaDbSchemaUpgradeTests()
    {
        _context = new DbContext(new DbContextOptionsBuilder().UseSqlite($"Data Source={_databasePath};Pooling=False").Options);
        _context.Database.ExecuteSqlRaw($"CREATE TABLE {Table} (Id TEXT PRIMARY KEY)");
    }

    public void Dispose()
    {
        _context.Dispose();
        File.Delete(_databasePath);
    }

    [Fact]
    public void Apply_should_apply_a_change_that_is_missing()
    {
        var sut = new SqliteSchemaUpgrade();

        sut.Apply(_context.Database);

        Assert.True(HasNewColumn());
        Assert.Equal(1, sut.Waits);
    }

    [Fact]
    public void Apply_should_neither_wait_for_other_instances_nor_change_anything_when_nothing_is_missing()
    {
        _context.Database.ExecuteSqlRaw(AddColumn);
        var sut = new SqliteSchemaUpgrade();

        sut.Apply(_context.Database);

        Assert.Equal(0, sut.Waits);
    }

    [Fact]
    public void Apply_should_not_apply_a_change_another_instance_applied_while_this_one_waited_for_it()
    {
        // applying it twice fails: the column would be there already
        var sut = new SqliteSchemaUpgrade { WhileWaiting = database => database.ExecuteSqlRaw(AddColumn) };

        sut.Apply(_context.Database);

        Assert.True(HasNewColumn());
        Assert.Equal(1, sut.Waits);
    }

    [Fact]
    public void Apply_should_say_so_when_the_database_does_not_have_the_tables()
    {
        _context.Database.ExecuteSqlRaw($"DROP TABLE {Table}");
        var sut = new SqliteSchemaUpgrade();

        var failure = Assert.Throws<InvalidOperationException>(() => sut.Apply(_context.Database));

        Assert.Contains("not the ones of OpenSleigh", failure.Message);
        Assert.Equal(0, sut.Waits);
    }

    [Fact]
    public void Apply_should_report_why_a_change_failed_and_how_to_apply_it_by_hand()
    {
        var sut = new SqliteSchemaUpgrade { WhileWaiting = _ => throw new TimeoutException("the lock was not granted") };

        var failure = Assert.Throws<InvalidOperationException>(() => sut.Apply(_context.Database));

        Assert.Contains(SqliteSchemaUpgrade.ChangeDescription, failure.Message);
        Assert.Contains("the lock was not granted", failure.Message);
        Assert.Contains(AddColumn, failure.Message);
        Assert.DoesNotContain("not allowed", failure.Message);
        Assert.IsType<TimeoutException>(failure.InnerException);
    }

    [Fact]
    public void Apply_should_report_a_missing_permission_as_such()
    {
        var sut = new SqliteSchemaUpgrade { WhileWaiting = _ => throw new UnauthorizedAccessException("denied") };

        var failure = Assert.Throws<InvalidOperationException>(() => sut.Apply(_context.Database));

        Assert.Contains("not allowed to change the schema", failure.Message);
        Assert.Contains(AddColumn, failure.Message);
    }

    [Fact]
    public void Apply_should_give_its_statements_more_time_than_a_query_and_restore_the_timeout()
    {
        const int applicationTimeout = 7;
        _context.Database.SetCommandTimeout(applicationTimeout);
        int? timeoutDuringTheUpgrade = null;
        var sut = new SqliteSchemaUpgrade { WhileWaiting = database => timeoutDuringTheUpgrade = database.GetCommandTimeout() };

        sut.Apply(_context.Database);

        Assert.True(timeoutDuringTheUpgrade > applicationTimeout);
        Assert.Equal(applicationTimeout, _context.Database.GetCommandTimeout());
    }

    private bool HasNewColumn()
        => _context.Database.SqlQueryRaw<int>(SqliteSchemaUpgrade.HasNewColumnQuery).AsEnumerable().Single() == 1;

    private sealed class SqliteSchemaUpgrade : SagaDbSchemaUpgrade
    {
        public const string ChangeDescription = "a due time";

        public const string HasNewColumnQuery =
            $"SELECT (SELECT COUNT(*) FROM pragma_table_info('{Table}') WHERE name = '{NewColumn}') AS \"{ResultColumn}\"";

        public int Waits { get; private set; }

        /// <summary>
        /// what happens between the moment this instance finds the change missing and the moment it has the lock.
        /// </summary>
        public Action<DatabaseFacade> WhileWaiting { get; init; } = _ => { };

        public override IReadOnlyList<SagaDbSchemaChange> Changes { get; } =
            [new SagaDbSchemaChange(ChangeDescription, HasNewColumnQuery, [AddColumn])];

        protected override string TablesExistQuery =>
            $"SELECT (SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{Table}') AS \"{ResultColumn}\"";

        protected override bool IsPermissionDenied(Exception exception)
            => exception is UnauthorizedAccessException;

        protected override void RunExclusively(DatabaseFacade database, SagaDbSchemaChange change, Action apply)
        {
            Waits++;
            WhileWaiting(database);
            apply();
        }
    }
}
