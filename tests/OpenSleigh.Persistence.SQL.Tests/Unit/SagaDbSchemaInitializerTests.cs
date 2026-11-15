using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Collections.Generic;
using System.IO;
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
    public void EnsureCreated_should_create_the_tables_of_a_relational_database_one_instance_at_a_time()
    {
        var upgrade = new RecordingSchemaUpgrade();
        var sut = new SagaDbSchemaInitializer(upgrade);
        using var database = new SqliteFile();
        using var context = database.CreateContext();

        sut.EnsureCreated(context.Database);

        Assert.Equal(1, upgrade.ExclusiveCreations);
        Assert.Equal(0, upgrade.Applied);
        Assert.False(context.Set<Entities.SagaState>().Any());
    }

    [Fact]
    public void EnsureCreated_should_not_upgrade_a_database_another_instance_created_while_this_one_waited_for_it()
    {
        using var database = new SqliteFile();
        var upgrade = new RecordingSchemaUpgrade
        {
            WhileWaiting = () =>
            {
                using var otherInstance = database.CreateContext();
                otherInstance.Database.EnsureCreated();
            }
        };
        var sut = new SagaDbSchemaInitializer(upgrade);
        using var context = database.CreateContext();

        sut.EnsureCreated(context.Database);

        Assert.Equal(1, upgrade.ExclusiveCreations);
        Assert.Equal(0, upgrade.Applied);
    }

    [Fact]
    public void EnsureCreated_should_work_inside_a_transaction_of_whoever_asks_for_the_first_context()
    {
        var sut = new SagaDbSchemaInitializer(new RecordingSchemaUpgrade());
        using var database = new SqliteFile();
        using var context = database.CreateContext();

        using (new System.Transactions.TransactionScope())
            sut.EnsureCreated(context.Database);

        Assert.False(context.Set<Entities.SagaState>().Any());
    }

    [Fact]
    public void EnsureCreated_should_run_the_seeding_the_context_was_configured_with()
    {
        var seeded = new List<bool>();
        using var database = new SqliteFile();
        using var context = database.CreateContext(options => options.UseSeeding((_, created) => seeded.Add(created)));

        new SagaDbSchemaInitializer(new RecordingSchemaUpgrade()).EnsureCreated(context.Database);
        new SagaDbSchemaInitializer(new RecordingSchemaUpgrade()).EnsureCreated(context.Database);

        Assert.Equal([true, false], seeded);
    }

    private sealed class SqliteFile : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"osl_create_{Guid.NewGuid():N}.db");

        public DbContext CreateContext(Action<DbContextOptionsBuilder>? configure = null)
        {
            var options = new DbContextOptionsBuilder<NotInitializedSagaDbContext>().UseSqlite($"Data Source={_path};Pooling=False");
            configure?.Invoke(options);
            return new NotInitializedSagaDbContext(options.Options);
        }

        public void Dispose() => File.Delete(_path);
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
        public int ExclusiveCreations { get; private set; }
        public int FailuresLeft { get; set; }

        /// <summary>what happens between the moment this instance asks for its turn and the moment it has it.</summary>
        public Action WhileWaiting { get; init; } = () => { };

        public override IReadOnlyList<SagaDbSchemaChange> Changes => [];
        protected override string TablesExistQuery => string.Empty;
        protected override bool IsPermissionDenied(Exception exception) => false;

        protected override void RunExclusively(DatabaseFacade database, Action createTables)
        {
            ExclusiveCreations++;
            WhileWaiting();
            createTables();
        }

        public override void Apply(DatabaseFacade database)
        {
            Applied++;
            if (FailuresLeft-- > 0)
                throw new InvalidOperationException("the upgrade failed");
        }
    }
}
