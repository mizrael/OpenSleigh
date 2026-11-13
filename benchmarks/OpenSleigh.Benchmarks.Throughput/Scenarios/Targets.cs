using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using OpenSleigh.Benchmarks.Throughput.Infrastructure;
using OpenSleigh.DependencyInjection;
using OpenSleigh.InMemory;
using OpenSleigh.Persistence.Mongo;
using OpenSleigh.Persistence.PostgreSQL;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Persistence.SQLServer;
using OpenSleigh.Transport.Kafka;
using OpenSleigh.Transport.RabbitMQ;

namespace OpenSleigh.Benchmarks.Throughput.Scenarios;

/// <summary>
/// A persistence provider wired through its public extension method, on a database unique to the run.
/// </summary>
internal sealed record PersistenceTarget(string Name, Action<IBusConfigurator> Configure, Func<IServiceProvider, Task> DropAsync)
{
    /// <summary>
    /// Runs before the host starts. The SQL providers create their schema from whichever DbContext comes first,
    /// and on a new database several of them racing at startup can fail: the schema is created up front instead.
    /// </summary>
    public Action<IServiceProvider> Prepare { get; init; } = _ => { };

    /// <param name="outboxBatchSize">rows the relay reads at once; the provider's default when omitted.</param>
    public static PersistenceTarget SqlServer(ScenarioContext context, int? outboxBatchSize = null)
    {
        var connectionString = string.Format(context.Settings.SqlServer, DatabaseName(context));
        return new PersistenceTarget(
            "sqlserver",
            cfg => cfg.UseSqlServerPersistence(SqlConfigurationFor(connectionString, outboxBatchSize)),
            DropSqlDatabaseAsync)
        { Prepare = CreateSqlSchema };
    }

    /// <param name="outboxBatchSize">rows the relay reads at once; the provider's default when omitted.</param>
    public static PersistenceTarget Postgres(ScenarioContext context, int? outboxBatchSize = null)
    {
        var connectionString = string.Format(context.Settings.Postgres, DatabaseName(context));
        return new PersistenceTarget(
            "postgres",
            cfg => cfg.UsePostgreSqlPersistence(SqlConfigurationFor(connectionString, outboxBatchSize)),
            DropSqlDatabaseAsync)
        { Prepare = CreateSqlSchema };
    }

    public static PersistenceTarget Mongo(ScenarioContext context)
    {
        var databaseName = DatabaseName(context);
        return new PersistenceTarget(
            "mongo",
            cfg => cfg.UseMongoPersistence(new MongoConfiguration(context.Settings.Mongo, databaseName)),
            services => services.GetRequiredService<IMongoClient>().DropDatabaseAsync(databaseName));
    }

    public static PersistenceTarget InMemory(ScenarioContext context)
        => new("inmemory", cfg => cfg.UseInMemoryPersistence(), _ => Task.CompletedTask);

    /// <summary>
    /// The SQL saga state repository on a local SQLite file: the library's own EF Core code, without a server.
    /// </summary>
    /// <param name="pooled">keep connections open between operations, so that what is measured is the client side.</param>
    public static PersistenceTarget SqliteSagaState(ScenarioContext context, bool pooled = false)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{DatabaseName(context)}.db");
        return new PersistenceTarget(
            "sqlite",
            cfg =>
            {
                cfg.Services
                    .AddSingleton(SqlSagaStateRepositoryOptions.Default)
                    .AddSingleton<SagaDbSchemaInitializer>()
                    .AddTransient<ISagaStateRepository, SqlSagaStateRepository>();
                AddSqliteDbContext(cfg.Services, $"Data Source={databasePath};Pooling={pooled}");
            },
            _ =>
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                File.Delete(databasePath);
                return Task.CompletedTask;
            });
    }

    private static string DatabaseName(ScenarioContext context) => $"osb_{context.RunId}";

    // the context is registered the way the SQL providers of the checkout being measured register theirs
    private static void AddSqliteDbContext(IServiceCollection services, string connectionString)
    {
#if SAGA_DBCONTEXT_POOL
        services.AddPooledSagaDbContext(builder => builder.UseSqlite(connectionString));
#else
        services.AddDbContext<SagaDbContext>(builder => builder.UseSqlite(connectionString), contextLifetime: ServiceLifetime.Transient);
#endif
    }

    private static SqlConfiguration SqlConfigurationFor(string connectionString, int? outboxBatchSize)
        => outboxBatchSize is null
            ? new SqlConfiguration(connectionString)
            : new SqlConfiguration(
                connectionString,
                SqlSagaStateRepositoryOptions.Default,
                SqlOutboxRepositoryOptions.Default with { MaxMessagesToPull = outboxBatchSize.Value });

    private static void CreateSqlSchema(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<SagaDbContext>().Database.EnsureCreated();
    }

    private static async Task DropSqlDatabaseAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SagaDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
    }
}

/// <summary>
/// A transport wired through its public extension method, on exchanges/topics unique to the run.
/// </summary>
internal sealed record TransportTarget(string Name, Action<IBusConfigurator> Configure, Func<Task> PrepareAsync)
{
    private static readonly TimeSpan RabbitRetryDelay = TimeSpan.FromMilliseconds(500);

    public static TransportTarget Rabbit(ScenarioContext context)
        => Rabbit(context, configuration => configuration);

#if RABBIT_CONSUMER_TUNING
    public static TransportTarget RabbitConcurrent(ScenarioContext context, ushort concurrency, ushort prefetchCount)
        => Rabbit(context, configuration => configuration with
        {
            ConsumerDispatchConcurrency = concurrency,
            PrefetchCount = prefetchCount
        });
#endif

    private static TransportTarget Rabbit(ScenarioContext context, Func<RabbitConfiguration, RabbitConfiguration> tune)
    {
        var settings = context.Settings;
        var prefix = $"bench.{context.RunId}";
        var configuration = tune(new RabbitConfiguration(
            settings.RabbitHost,
            settings.RabbitUser,
            settings.RabbitPassword,
            settings.RabbitVirtualHost,
            RabbitRetryDelay));

        OpenSleigh.Transport.RabbitMQ.QueueReferencesCreator creator = messageType =>
        {
            var exchangeName = $"{prefix}.{messageType.Name}".ToLowerInvariant();
            return new OpenSleigh.Transport.RabbitMQ.QueueReferences(
                exchangeName,
                $"{exchangeName}.workers",
                $"{exchangeName}.dead",
                $"{exchangeName}.dead.workers");
        };

        return new TransportTarget("rabbit", cfg => cfg.UseRabbitMQTransport(configuration, creator), () => Task.CompletedTask);
    }

    public static TransportTarget Kafka(ScenarioContext context, params Type[] messageTypes)
    {
        var prefix = $"bench.{context.RunId}";
        string TopicName(Type messageType) => $"{prefix}.{messageType.Name}".ToLowerInvariant();

        var configuration = new KafkaConfiguration(
            context.Settings.Kafka,
            messageType => new OpenSleigh.Transport.Kafka.QueueReferences(TopicName(messageType), $"{TopicName(messageType)}.dead"));

        return new TransportTarget(
            "kafka",
            cfg => cfg.UseKafkaTransport(configuration),
            () => CreateTopicsAsync(context.Settings.Kafka, messageTypes.Select(TopicName)));
    }

    public static TransportTarget InMemory()
        => new("inmemory", cfg => cfg.UseInMemoryTransport(), () => Task.CompletedTask);

    // topics are created up front: a consumer that subscribes before its topic exists
    // only notices it at the next metadata refresh, which would end up inside the measurement
    private static async Task CreateTopicsAsync(string bootstrapServers, IEnumerable<string> topicNames)
    {
        using var adminClient = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers }).Build();
        var specifications = topicNames.Select(name => new TopicSpecification
        {
            Name = name,
            NumPartitions = 1,
            ReplicationFactor = 1
        });
        await adminClient.CreateTopicsAsync(specifications);
    }
}
