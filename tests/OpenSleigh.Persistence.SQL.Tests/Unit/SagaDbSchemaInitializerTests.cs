using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Collections.Generic;
using System.Linq;

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
        using var context = CreateExistingDatabase(Guid.NewGuid().ToString());

        sut.EnsureCreated(context.Database);
        sut.EnsureCreated(context.Database);

        Assert.Equal(1, upgrade.Applied);
    }

    [Fact]
    public void EnsureCreated_should_try_again_after_an_upgrade_that_failed()
    {
        var upgrade = new RecordingSchemaUpgrade { FailuresLeft = 1 };
        var sut = new SagaDbSchemaInitializer(upgrade);
        using var context = CreateExistingDatabase(Guid.NewGuid().ToString());

        Assert.Throws<InvalidOperationException>(() => sut.EnsureCreated(context.Database));
        sut.EnsureCreated(context.Database);

        Assert.Equal(2, upgrade.Applied);
    }

    [Fact]
    public async Task The_schema_should_be_upgraded_when_the_host_starts_before_any_hosted_service_does()
    {
        var databaseName = Guid.NewGuid().ToString();
        using var existing = CreateExistingDatabase(databaseName);
        await using var provider = BuildProvider(databaseName);
        var upgrade = (RecordingSchemaUpgrade)provider.GetRequiredService<SagaDbSchemaUpgrade>();

        await StartingAsync(provider);

        Assert.Equal(1, upgrade.Applied);
    }

    [Fact]
    public async Task A_schema_that_cannot_be_upgraded_should_stop_the_host_from_starting()
    {
        var databaseName = Guid.NewGuid().ToString();
        using var existing = CreateExistingDatabase(databaseName);
        await using var provider = BuildProvider(databaseName);
        var upgrade = (RecordingSchemaUpgrade)provider.GetRequiredService<SagaDbSchemaUpgrade>();
        upgrade.FailuresLeft = 1;

        await Assert.ThrowsAsync<InvalidOperationException>(() => StartingAsync(provider));
    }

    private static ServiceProvider BuildProvider(string databaseName)
        => new ServiceCollection()
            .AddSagaDbSchemaInitialization<RecordingSchemaUpgrade>()
            .AddPooledSagaDbContext(builder => builder.UseInMemoryDatabase(databaseName))
            .BuildServiceProvider();

    // what the host does with the services that take part in its start, before it starts any of them
    private static async Task StartingAsync(IServiceProvider provider)
    {
        foreach (var service in provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>())
            await service.StartingAsync(CancellationToken.None);
    }

    private static DbContext CreateExistingDatabase(string databaseName)
    {
        var context = CreateContext(databaseName);
        context.Database.EnsureCreated();
        return context;
    }

    // the contexts of a service provider find the database of this one: in-memory databases are known by name
    private static DbContext CreateContext(string databaseName)
        => new NotInitializedSagaDbContext(
            new DbContextOptionsBuilder<NotInitializedSagaDbContext>().UseInMemoryDatabase(databaseName).Options);

    private sealed class NotInitializedSagaDbContext(DbContextOptions<NotInitializedSagaDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.ApplyConfigurationsFromAssembly(typeof(SagaDbContext).Assembly);
    }

    private sealed class RecordingSchemaUpgrade : SagaDbSchemaUpgrade
    {
        public int Applied { get; private set; }
        public int FailuresLeft { get; set; }

        public override IReadOnlyList<SagaDbSchemaChange> Changes => [];
        protected override string TablesExistQuery => string.Empty;
        protected override bool IsPermissionDenied(Exception exception) => false;

        protected override void RunExclusively(DatabaseFacade database, SagaDbSchemaChange change, Action apply)
            => apply();

        public override void Apply(DatabaseFacade database)
        {
            Applied++;
            if (FailuresLeft-- > 0)
                throw new InvalidOperationException("the upgrade failed");
        }
    }
}
