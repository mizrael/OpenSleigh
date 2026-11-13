using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Collections.Generic;

namespace OpenSleigh.Persistence.SQL.Tests.Unit;

public class SagaDbSchemaInitializerTests
{
    [Fact]
    public void EnsureCreated_should_not_upgrade_a_database_it_has_just_created()
    {
        var upgrade = new RecordingSchemaUpgrade();
        var sut = new SagaDbSchemaInitializer(upgrade);
        using var context = CreateContext(Guid.NewGuid().ToString());

        sut.EnsureCreated(context.Database);

        Assert.Equal(0, upgrade.Applied);
    }

    [Fact]
    public void EnsureCreated_should_upgrade_a_database_that_already_existed_once()
    {
        var upgrade = new RecordingSchemaUpgrade();
        var sut = new SagaDbSchemaInitializer(upgrade);
        using var context = CreateExistingDatabase();

        sut.EnsureCreated(context.Database);
        sut.EnsureCreated(context.Database);

        Assert.Equal(1, upgrade.Applied);
    }

    [Fact]
    public void EnsureCreated_should_try_again_after_an_upgrade_that_failed()
    {
        var upgrade = new RecordingSchemaUpgrade { FailuresLeft = 1 };
        var sut = new SagaDbSchemaInitializer(upgrade);
        using var context = CreateExistingDatabase();

        Assert.Throws<InvalidOperationException>(() => sut.EnsureCreated(context.Database));
        sut.EnsureCreated(context.Database);

        Assert.Equal(2, upgrade.Applied);
    }

    [Fact]
    public void EnsureCreated_should_leave_an_existing_database_alone_without_an_upgrade()
    {
        var sut = new SagaDbSchemaInitializer();
        using var context = CreateExistingDatabase();

        sut.EnsureCreated(context.Database);
    }

    private static DbContext CreateExistingDatabase()
    {
        var context = CreateContext(Guid.NewGuid().ToString());
        context.Database.EnsureCreated();
        return context;
    }

    private static DbContext CreateContext(string databaseName)
        => new(new DbContextOptionsBuilder().UseInMemoryDatabase(databaseName).Options);

    private sealed class RecordingSchemaUpgrade : SagaDbSchemaUpgrade
    {
        public int Applied { get; private set; }
        public int FailuresLeft { get; set; }

        protected override string IsUpToDateQuery => string.Empty;
        protected override string ConcurrentUpgradesLockStatement => string.Empty;
        public override IReadOnlyList<string> Statements => [];

        public override void Apply(DatabaseFacade database)
        {
            Applied++;
            if (FailuresLeft-- > 0)
                throw new InvalidOperationException("the upgrade failed");
        }
    }
}
