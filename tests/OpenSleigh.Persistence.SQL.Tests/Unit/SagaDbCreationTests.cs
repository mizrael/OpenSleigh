using Microsoft.EntityFrameworkCore.Storage;

namespace OpenSleigh.Persistence.SQL.Tests.Unit;

/// <summary>
/// what an instance does when others create the same database at the same time, on a substitute of the creator:
/// the races themselves are run against PostgreSQL and SQL Server by the integration tests.
/// </summary>
public class SagaDbCreationTests
{
    private static readonly TimeSpan NoWait = TimeSpan.Zero;

    [Fact]
    public void EnsureCreated_should_create_the_database_and_its_tables_when_both_are_missing()
    {
        var creator = Substitute.For<IRelationalDatabaseCreator>();
        var exclusiveRuns = 0;

        var created = new SagaDbCreation(creator, NoWait).EnsureCreated(createTables =>
        {
            exclusiveRuns++;
            createTables();
        });

        Assert.True(created);
        Assert.Equal(1, exclusiveRuns);
        creator.Received(1).Create();
        creator.Received(1).CreateTables();
    }

    [Fact]
    public void EnsureCreated_should_go_on_when_another_instance_created_the_database_first()
    {
        var creator = Substitute.For<IRelationalDatabaseCreator>();
        creator.Exists().Returns(false, true);
        creator.When(c => c.Create()).Do(_ => throw new InvalidOperationException("the database already exists"));

        var created = new SagaDbCreation(creator, NoWait).EnsureCreated(createTables => createTables());

        Assert.True(created);
        creator.Received(1).CreateTables();
    }

    [Fact]
    public void EnsureCreated_should_fail_when_the_database_cannot_be_created_and_nobody_else_creates_it()
    {
        var creator = Substitute.For<IRelationalDatabaseCreator>();
        creator.When(c => c.Create()).Do(_ => throw new InvalidOperationException("not allowed"));

        var failure = Assert.Throws<InvalidOperationException>(
            () => new SagaDbCreation(creator, NoWait).EnsureCreated(createTables => createTables()));

        Assert.Equal("not allowed", failure.Message);
        creator.DidNotReceive().CreateTables();
    }

    [Fact]
    public void EnsureCreated_should_not_create_the_tables_another_instance_created_while_this_one_waited_for_it()
    {
        var creator = Substitute.For<IRelationalDatabaseCreator>();
        creator.Exists().Returns(true);

        var created = new SagaDbCreation(creator, NoWait).EnsureCreated(createTables =>
        {
            creator.HasTables().Returns(true);
            createTables();
        });

        Assert.False(created);
        creator.DidNotReceive().CreateTables();
    }

    [Fact]
    public void EnsureCreated_should_neither_wait_for_other_instances_nor_check_more_than_before_when_the_tables_exist()
    {
        var creator = Substitute.For<IRelationalDatabaseCreator>();
        creator.Exists().Returns(true);
        creator.HasTables().Returns(true);

        var created = new SagaDbCreation(creator, NoWait)
            .EnsureCreated(_ => throw new InvalidOperationException("nothing to wait for"));

        Assert.False(created);
        creator.Received(1).Exists();
        creator.Received(1).HasTables();
        creator.DidNotReceive().Create();
        creator.DidNotReceive().CreateTables();
    }
}
