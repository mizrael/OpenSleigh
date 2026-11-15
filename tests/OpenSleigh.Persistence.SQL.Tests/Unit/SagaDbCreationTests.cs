using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace OpenSleigh.Persistence.SQL.Tests.Unit;

/// <summary>
/// what an instance does when others create the same database at the same time, on a substitute of the creator:
/// the races themselves are run against PostgreSQL and SQL Server by the integration tests.
/// </summary>
public class SagaDbCreationTests
{
    private static readonly TimeSpan NoWait = TimeSpan.Zero;

    private sealed class AlreadyExistsException() : Exception("the database already exists");

    private static SagaDbCreation CreateSut(
        IRelationalDatabaseCreator creator,
        Action? forgetFailedConnections = null,
        ILogger? logger = null,
        TimeSpan? databaseWait = null)
        => new(creator, exception => exception is AlreadyExistsException, forgetFailedConnections, logger: logger, databaseWait: databaseWait ?? NoWait);

    [Fact]
    public void EnsureCreated_should_create_the_database_and_its_tables_when_both_are_missing()
    {
        var creator = Substitute.For<IRelationalDatabaseCreator>();
        var exclusiveRuns = 0;

        var outcome = CreateSut(creator).EnsureCreated(createTables =>
        {
            exclusiveRuns++;
            createTables();
        });

        Assert.Equal(SagaDbCreationOutcome.Created, outcome);
        Assert.Equal(1, exclusiveRuns);
        creator.Received(1).Create();
        creator.Received(1).CreateTables();
    }

    [Fact]
    public void EnsureCreated_should_go_on_and_say_so_when_another_instance_created_the_database_first()
    {
        var creator = Substitute.For<IRelationalDatabaseCreator>();
        creator.Exists().Returns(false, false, true);
        creator.When(c => c.Create()).Do(_ => throw new AlreadyExistsException());
        var logger = Substitute.For<ILogger>();

        var outcome = CreateSut(creator, logger: logger).EnsureCreated(createTables => createTables());

        Assert.Equal(SagaDbCreationOutcome.Created, outcome);
        creator.Received(1).CreateTables();
        Assert.Equal(LogLevel.Information, Assert.Single(logger.ReceivedCalls()).GetArguments().OfType<LogLevel>().Single());
    }

    [Fact]
    public void EnsureCreated_should_report_at_once_a_failure_that_is_not_another_instance_creating_the_database()
    {
        var creator = Substitute.For<IRelationalDatabaseCreator>();
        creator.When(c => c.Create()).Do(_ => throw new InvalidOperationException("not allowed"));

        var failure = Assert.Throws<InvalidOperationException>(
            () => CreateSut(creator, databaseWait: TimeSpan.FromMinutes(1)).EnsureCreated(createTables => createTables()));

        Assert.Equal("not allowed", failure.Message);

        // before it tried, and once it had its turn to try: not again after the failure
        creator.Received(2).Exists();
        creator.DidNotReceive().CreateTables();
    }

    [Fact]
    public void EnsureCreated_should_report_the_failure_when_the_database_another_instance_is_creating_never_shows_up()
    {
        var creator = Substitute.For<IRelationalDatabaseCreator>();
        creator.When(c => c.Create()).Do(_ => throw new AlreadyExistsException());

        Assert.Throws<AlreadyExistsException>(() => CreateSut(creator).EnsureCreated(createTables => createTables()));

        creator.DidNotReceive().CreateTables();
    }

    [Fact]
    public void EnsureCreated_should_stop_waiting_for_the_database_when_it_is_cancelled()
    {
        var creator = Substitute.For<IRelationalDatabaseCreator>();
        creator.When(c => c.Create()).Do(_ => throw new AlreadyExistsException());
        using var stopping = new CancellationTokenSource();
        stopping.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => CreateSut(creator, databaseWait: TimeSpan.FromMinutes(1)).EnsureCreated(createTables => createTables(), stopping.Token));
    }

    [Fact]
    public void EnsureCreated_should_forget_failed_connections_before_it_asks_again_whether_the_database_exists()
    {
        var creator = Substitute.For<IRelationalDatabaseCreator>();
        var forgotten = 0;

        // what a connection pool that remembers a failed login does: the same answer, until it is told to forget.
        // Once before this instance tries to create the database, and again when it waits for the one that is
        creator.Exists().Returns(_ => forgotten >= 2);
        creator.When(c => c.Create()).Do(_ => throw new AlreadyExistsException());

        var outcome = CreateSut(creator, forgetFailedConnections: () => forgotten++).EnsureCreated(createTables => createTables());

        Assert.Equal(SagaDbCreationOutcome.Created, outcome);
        creator.Received(1).Create();
    }

    [Fact]
    public void EnsureCreated_should_take_a_database_that_cannot_be_asked_about_as_missing()
    {
        var creator = Substitute.For<IRelationalDatabaseCreator>();
        creator.Exists().Returns(_ => throw new InvalidOperationException("the server refused the connection"), _ => false, _ => true);
        creator.When(c => c.Create()).Do(_ => throw new AlreadyExistsException());

        var outcome = CreateSut(creator).EnsureCreated(createTables => createTables());

        Assert.Equal(SagaDbCreationOutcome.Created, outcome);
    }

    [Fact]
    public void EnsureCreated_should_not_create_the_database_another_instance_created_while_this_one_waited_for_it()
    {
        var creator = Substitute.For<IRelationalDatabaseCreator>();
        var forgotten = false;
        creator.Exists().Returns(_ => forgotten);
        var sut = new SagaDbCreation(
            creator,
            _ => false,
            forgetFailedConnections: () => forgotten = true,
            runDatabaseCreationExclusively: createDatabase => createDatabase());

        var outcome = sut.EnsureCreated(createTables => createTables());

        Assert.Equal(SagaDbCreationOutcome.Created, outcome);
        creator.DidNotReceive().Create();
        creator.Received(1).CreateTables();
    }

    [Fact]
    public void EnsureCreated_should_not_create_the_tables_another_instance_created_while_this_one_waited_for_it()
    {
        var creator = Substitute.For<IRelationalDatabaseCreator>();
        creator.Exists().Returns(true);

        var outcome = CreateSut(creator).EnsureCreated(createTables =>
        {
            creator.HasTables().Returns(true);
            createTables();
        });

        Assert.Equal(SagaDbCreationOutcome.CreatedByAnotherInstance, outcome);
        creator.DidNotReceive().CreateTables();
    }

    [Fact]
    public void EnsureCreated_should_neither_wait_for_other_instances_nor_check_more_than_before_when_the_tables_exist()
    {
        var creator = Substitute.For<IRelationalDatabaseCreator>();
        creator.Exists().Returns(true);
        creator.HasTables().Returns(true);

        var outcome = CreateSut(creator).EnsureCreated(_ => throw new InvalidOperationException("nothing to wait for"));

        Assert.Equal(SagaDbCreationOutcome.AlreadyThere, outcome);
        creator.Received(1).Exists();
        creator.Received(1).HasTables();
        creator.DidNotReceive().Create();
        creator.DidNotReceive().CreateTables();
    }
}
