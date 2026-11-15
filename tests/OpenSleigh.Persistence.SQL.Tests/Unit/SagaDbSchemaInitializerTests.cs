using Microsoft.EntityFrameworkCore.Diagnostics;
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

        Assert.Equal(1, upgrade.ExclusiveDatabaseCreations);
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
        var seeded = new List<bool>();
        using var context = database.CreateContext(options => options.UseSeeding((_, created) => seeded.Add(created)));

        sut.EnsureCreated(context.Database);

        Assert.Equal(1, upgrade.ExclusiveCreations);
        Assert.Equal(0, upgrade.Applied);
        Assert.Equal([false], seeded);
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

    [Fact]
    public void EnsureCreated_should_seed_in_a_transaction_once_the_tables_are_there_and_current()
    {
        const string seededTable = "SELECT COUNT(*) AS \"Value\" FROM sqlite_master WHERE name = 'seeded'";
        using var database = new SqliteFile();
        using (var existing = database.CreateContext())
            existing.Database.EnsureCreated();

        var upgrade = new RecordingSchemaUpgrade();
        var appliedWhenSeeding = -1;
        using var context = database.CreateContext(options => options.UseSeeding((seeding, _) =>
        {
            appliedWhenSeeding = upgrade.Applied;
            seeding.Database.ExecuteSqlRaw("CREATE TABLE seeded (id INTEGER)");
            throw new InvalidOperationException("the seeding failed");
        }));

        Assert.Throws<InvalidOperationException>(() => new SagaDbSchemaInitializer(upgrade).EnsureCreated(context.Database));

        Assert.Equal(1, appliedWhenSeeding);
        Assert.Equal(0, context.Database.SqlQueryRaw<int>(seededTable).AsEnumerable().Single());
    }

    [Fact]
    public void EnsureCreated_should_refuse_a_context_that_can_only_be_seeded_asynchronously()
    {
        using var database = new SqliteFile();
        using var context = database.CreateContext(options => options.UseAsyncSeeding((_, _, _) => Task.CompletedTask));

        Assert.Throws<InvalidOperationException>(
            () => new SagaDbSchemaInitializer(new RecordingSchemaUpgrade()).EnsureCreated(context.Database));
    }

    [Fact]
    public void EnsureCreated_should_seed_tables_that_were_there_while_no_other_instance_does()
    {
        using var database = new SqliteFile();
        using (var existing = database.CreateContext())
            existing.Database.EnsureCreated();

        var upgrade = new RecordingSchemaUpgrade();
        var exclusiveWhenSeeding = -1;
        using var context = database.CreateContext(
            options => options.UseSeeding((_, _) => exclusiveWhenSeeding = upgrade.ExclusiveSectionsRunning));

        new SagaDbSchemaInitializer(upgrade).EnsureCreated(context.Database);

        Assert.Equal(1, exclusiveWhenSeeding);
    }

    [Fact]
    public void EnsureCreated_should_not_wait_for_other_instances_when_the_tables_are_there_and_nothing_seeds_them()
    {
        using var database = new SqliteFile();
        using (var existing = database.CreateContext())
            existing.Database.EnsureCreated();

        var upgrade = new RecordingSchemaUpgrade();
        using var context = database.CreateContext();

        new SagaDbSchemaInitializer(upgrade).EnsureCreated(context.Database);

        Assert.Equal(0, upgrade.ExclusiveCreations);
        Assert.Equal(0, upgrade.ExclusiveDatabaseCreations);
    }

    [Fact]
    public void EnsureCreated_should_give_the_creation_of_the_database_more_time_than_a_query_and_restore_the_timeout()
    {
        const int applicationTimeout = 7;
        var upgrade = new RecordingSchemaUpgrade();
        using var database = new SqliteFile();
        using var context = database.CreateContext();
        context.Database.SetCommandTimeout(applicationTimeout);

        new SagaDbSchemaInitializer(upgrade).EnsureCreated(context.Database);

        Assert.True(upgrade.TimeoutWhenCreatingTheDatabase > applicationTimeout);
        Assert.Equal(applicationTimeout, context.Database.GetCommandTimeout());
    }

    [Fact]
    public void EnsureCreated_should_fail_and_try_again_when_the_lock_to_create_the_database_cannot_be_taken()
    {
        var upgrade = new RecordingSchemaUpgrade { DatabaseCreationLocksRefused = 1 };
        var sut = new SagaDbSchemaInitializer(upgrade);
        using var database = new SqliteFile();
        using var context = database.CreateContext();

        Assert.Throws<TimeoutException>(() => sut.EnsureCreated(context.Database));
        Assert.Equal(0, upgrade.ExclusiveCreations);

        sut.EnsureCreated(context.Database);

        Assert.False(context.Set<Entities.SagaState>().Any());
    }

    public static TheoryData<SagaDbSchemaUpgrade, Action<DbContextOptionsBuilder>, Func<string, bool>> ServersThatCannotBeReached => new()
    {
        {
            new SQLServer.SqlServerSchemaUpgrade(),
            options => options.UseSqlServer("Server=tcp:127.0.0.1,1;Database=missing;User Id=sa;Password=none;Connect Timeout=2;Encrypt=False"),
            connectionString => new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString) is { InitialCatalog: "master", Pooling: false }
        },
        {
            new PostgreSQL.PostgreSqlSchemaUpgrade(),
            options => options.UseNpgsql("Host=127.0.0.1;Port=1;Database=missing;Username=postgres;Password=none;Timeout=2"),
            connectionString => new Npgsql.NpgsqlConnectionStringBuilder(connectionString) is { Database: "postgres", Pooling: false }
        },
    };

    [Theory]
    [MemberData(nameof(ServersThatCannotBeReached))]
    public void The_lock_to_create_the_database_should_be_asked_for_on_a_connection_of_the_provider_that_is_not_pooled(
        SagaDbSchemaUpgrade upgrade, Action<DbContextOptionsBuilder> useServer, Func<string, bool> isUnpooledConnectionToTheSystemDatabase)
    {
        var connections = new RecordingConnectionInterceptor();
        var options = new DbContextOptionsBuilder<NotInitializedSagaDbContext>().AddInterceptors(connections);
        useServer(options);
        using var context = new NotInitializedSagaDbContext(options.Options);

        var failure = Assert.ThrowsAny<Exception>(
            () => upgrade.EnsureCreatedAndCurrent(context.Database, DatabaseThatIsMissing(), seed: null));

        // the failure to connect is reported, as it is or as the cause of what the provider reports
        Assert.Contains(Causes(failure), cause => cause is System.Data.Common.DbException);
        Assert.True(isUnpooledConnectionToTheSystemDatabase(Assert.Single(connections.Opening)));
    }

    [Fact]
    public void A_PostgreSQL_database_should_not_be_created_when_the_administration_database_is_its_template()
    {
        const string template = "template1";
        var connections = new RecordingConnectionInterceptor();
        var options = new DbContextOptionsBuilder<NotInitializedSagaDbContext>()
            .AddInterceptors(connections)
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=missing;Timeout=2", npgsql => npgsql.UseAdminDatabase(template));
        using var context = new NotInitializedSagaDbContext(options.Options);

        var failure = Assert.Throws<InvalidOperationException>(
            () => new PostgreSQL.PostgreSqlSchemaUpgrade().EnsureCreatedAndCurrent(context.Database, DatabaseThatIsMissing(), seed: null));

        Assert.Contains(template, failure.Message);
        Assert.Empty(connections.Opening);
    }

    private static Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator DatabaseThatIsMissing()
    {
        var creator = Substitute.For<Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator>();
        creator.Exists().Returns(false);
        return creator;
    }

    private sealed class RecordingConnectionInterceptor : DbConnectionInterceptor
    {
        public List<string> Opening { get; } = [];

        public override InterceptionResult ConnectionOpening(
            System.Data.Common.DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
        {
            Opening.Add(connection.ConnectionString);
            return result;
        }
    }

    private static IEnumerable<Exception> Causes(Exception? failure)
    {
        for (var current = failure; current is not null; current = current.InnerException)
            yield return current;
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
        public int ExclusiveDatabaseCreations { get; private set; }
        public int FailuresLeft { get; set; }
        public int DatabaseCreationLocksRefused { get; set; }
        public int ExclusiveSectionsRunning { get; private set; }
        public int? TimeoutWhenCreatingTheDatabase { get; private set; }

        /// <summary>what happens between the moment this instance asks for its turn and the moment it has it.</summary>
        public Action WhileWaiting { get; init; } = () => { };

        public override IReadOnlyList<SagaDbSchemaChange> Changes => [];
        protected override string TablesExistQuery => string.Empty;
        protected override bool IsPermissionDenied(Exception exception) => false;

        protected override void RunDatabaseCreationExclusively(DatabaseFacade database, Action createDatabase)
        {
            ExclusiveDatabaseCreations++;
            if (DatabaseCreationLocksRefused-- > 0)
                throw new TimeoutException("the lock was not granted");

            TimeoutWhenCreatingTheDatabase = database.GetCommandTimeout();
            createDatabase();
        }

        protected override void RunExclusively(DatabaseFacade database, Action createTables)
        {
            ExclusiveCreations++;
            WhileWaiting();

            ExclusiveSectionsRunning++;
            createTables();
            ExclusiveSectionsRunning--;
        }

        public override void Apply(DatabaseFacade database)
        {
            Applied++;
            if (FailuresLeft-- > 0)
                throw new InvalidOperationException("the upgrade failed");
        }
    }
}
