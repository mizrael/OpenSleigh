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

    public static PersistenceTarget SqlServer(ScenarioContext context, SqlTuning? tuning = null)
    {
        tuning ??= SqlTuning.None;
        var connectionString = string.Format(context.Settings.SqlServer, DatabaseName(context)) + tuning.ConnectionStringSuffix;
        return new PersistenceTarget(
            "sqlserver",
            cfg => cfg.UseSqlServerPersistence(tuning.Configuration(connectionString)),
            DropSqlDatabaseAsync)
        { Prepare = services => CreateSqlSchema(services, tuning) };
    }

    public static PersistenceTarget Postgres(ScenarioContext context, SqlTuning? tuning = null)
    {
        tuning ??= SqlTuning.None;
        var connectionString = string.Format(context.Settings.Postgres, DatabaseName(context)) + tuning.ConnectionStringSuffix;
        return new PersistenceTarget(
            "postgres",
            cfg => cfg.UsePostgreSqlPersistence(tuning.Configuration(connectionString)),
            DropSqlDatabaseAsync)
        { Prepare = services => CreateSqlSchema(services, tuning) };
    }

    /// <param name="connectionOptions">connection string options, such as "journal=true".</param>
    public static PersistenceTarget Mongo(ScenarioContext context, string? connectionOptions = null)
    {
        var databaseName = DatabaseName(context);
        var connectionString = string.IsNullOrEmpty(connectionOptions)
            ? context.Settings.Mongo
            : $"{context.Settings.Mongo.TrimEnd('/')}/?{connectionOptions}";
        return new PersistenceTarget(
            "mongo",
            cfg => cfg.UseMongoPersistence(new MongoConfiguration(connectionString, databaseName)),
            services => services.GetRequiredService<IMongoClient>().DropDatabaseAsync(databaseName));
    }

    public static PersistenceTarget InMemory(ScenarioContext context)
        => new("inmemory", cfg => cfg.UseInMemoryPersistence(), _ => Task.CompletedTask);

    /// <summary>
    /// The SQL saga state repository on a local SQLite file: the library's own EF Core code, without a server.
    /// </summary>
    public static PersistenceTarget SqliteSagaState(ScenarioContext context)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{DatabaseName(context)}.db");
        return new PersistenceTarget(
            "sqlite",
            cfg => cfg.Services
                .AddSingleton(SqlSagaStateRepositoryOptions.Default)
                .AddDbContext<SagaDbContext>(
                    builder => builder.UseSqlite($"Data Source={databasePath};Pooling=False"),
                    contextLifetime: ServiceLifetime.Transient)
                .AddTransient<ISagaStateRepository, SqlSagaStateRepository>(),
            _ =>
            {
                File.Delete(databasePath);
                return Task.CompletedTask;
            });
    }

    private static string DatabaseName(ScenarioContext context) => $"osb_{context.RunId}";

    private static void CreateSqlSchema(IServiceProvider services, SqlTuning tuning)
    {
        using var scope = services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<SagaDbContext>().Database;
        database.EnsureCreated();

        foreach (var statement in tuning.DatabaseStatements)
            database.ExecuteSqlRaw(statement);
    }

    private static async Task DropSqlDatabaseAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SagaDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
    }
}

/// <summary>
/// Settings of a SQL provider that an application can change without touching the library.
/// </summary>
/// <param name="ConnectionStringSuffix">keywords appended to the connection string.</param>
/// <param name="OutboxBatchSize">how many outbox rows the relay reads at once.</param>
/// <param name="Statements">database settings, applied once the schema exists.</param>
internal sealed record SqlTuning(string ConnectionStringSuffix = "", int? OutboxBatchSize = null, IReadOnlyList<string>? Statements = null)
{
    public static readonly SqlTuning None = new();

    public const string PostgresAsyncCommit = "Options=-c synchronous_commit=off;";
    public const string PostgresAutoPrepare = "Max Auto Prepare=50;Auto Prepare Min Usages=2;";
    public const string PostgresNoReset = "No Reset On Close=true;";
    public const string SqlServerNoEncryption = "Encrypt=False;";
    public const string SqlServerDelayedDurability = "ALTER DATABASE CURRENT SET DELAYED_DURABILITY = FORCED";
    public const string SqlServerSnapshotReads = "ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE";

    public IReadOnlyList<string> DatabaseStatements => Statements ?? [];

    public SqlConfiguration Configuration(string connectionString)
        => OutboxBatchSize is { } batchSize
            ? new SqlConfiguration(
                connectionString,
                SqlSagaStateRepositoryOptions.Default,
                SqlOutboxRepositoryOptions.Default with { MaxMessagesToPull = batchSize })
            : new SqlConfiguration(connectionString);
}

/// <summary>
/// A transport wired through its public extension method, on exchanges/topics unique to the run.
/// </summary>
internal sealed record TransportTarget(string Name, Action<IBusConfigurator> Configure, Func<Task> PrepareAsync)
{
    private static readonly TimeSpan RabbitRetryDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ConsumerGroupSettleTime = TimeSpan.FromSeconds(6);

    /// <summary>
    /// How long to wait after the warm-up before measuring: several consumers joining one group
    /// keep rebalancing for a while after the first of them has consumed a message.
    /// </summary>
    public TimeSpan SettleTime { get; init; } = TimeSpan.Zero;

    public static TransportTarget Rabbit(ScenarioContext context)
        => Rabbit(context, configuration => configuration);

#if RABBIT_CONSUMER_TUNING
    public static TransportTarget RabbitConcurrent(ScenarioContext context, ushort concurrency, ushort prefetchCount)
        => Rabbit(context, configuration => configuration with
        {
            ConsumerDispatchConcurrency = concurrency,
            PrefetchCount = prefetchCount
        });

    public static TransportTarget RabbitConfirming(ScenarioContext context, ushort concurrency, ushort prefetchCount)
        => Rabbit(
            context,
            configuration => configuration with
            {
                ConsumerDispatchConcurrency = concurrency,
                PrefetchCount = prefetchCount
            },
            (services, configuration) => services.AddSingleton<IChannelFactory>(provider => new ConfirmingChannelFactory(
                provider.GetRequiredService<RabbitMQ.Client.IConnectionFactory>(),
                configuration)));
#endif

    private static TransportTarget Rabbit(
        ScenarioContext context,
        Func<RabbitConfiguration, RabbitConfiguration> tune,
        Action<IServiceCollection, RabbitConfiguration>? overrideServices = null)
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

        return new TransportTarget("rabbit", cfg =>
        {
            cfg.UseRabbitMQTransport(configuration, creator);
            overrideServices?.Invoke(cfg.Services, configuration);
        }, () => Task.CompletedTask);
    }

    public static TransportTarget Kafka(ScenarioContext context, params Type[] messageTypes)
        => Kafka(context, tuneProducer: null, consumers: 1, messageTypes);

    /// <param name="tuneProducer">producer settings, applied by registering a ProducerConfig after the transport.</param>
    /// <param name="consumers">consumers in the group, and partitions per topic.</param>
    public static TransportTarget Kafka(ScenarioContext context, Action<ProducerConfig>? tuneProducer, int consumers, params Type[] messageTypes)
    {
        var prefix = $"bench.{context.RunId}";
        string TopicName(Type messageType) => $"{prefix}.{messageType.Name}".ToLowerInvariant();

        var configuration = new KafkaConfiguration(
            context.Settings.Kafka,
            messageType => new OpenSleigh.Transport.Kafka.QueueReferences(TopicName(messageType), $"{TopicName(messageType)}.dead"));

        return new TransportTarget(
            "kafka",
            cfg =>
            {
                cfg.UseKafkaTransport(configuration);

                if (tuneProducer is not null)
                    cfg.Services.AddSingleton(_ =>
                    {
                        var producerConfig = new ProducerConfig { BootstrapServers = context.Settings.Kafka };
                        tuneProducer(producerConfig);
                        return producerConfig;
                    });

                for (var extra = 1; extra < consumers; extra++)
                    cfg.Services.AddSingleton<OpenSleigh.Transport.IMessageSubscriber, KafkaMessageSubscriber>();
            },
            () => CreateTopicsAsync(context.Settings.Kafka, messageTypes.Select(TopicName), partitions: consumers))
        { SettleTime = consumers > 1 ? ConsumerGroupSettleTime : TimeSpan.Zero };
    }

    public static TransportTarget InMemory()
        => new("inmemory", cfg => cfg.UseInMemoryTransport(), () => Task.CompletedTask);

    // topics are created up front: a consumer that subscribes before its topic exists
    // only notices it at the next metadata refresh, which would end up inside the measurement
    private static async Task CreateTopicsAsync(string bootstrapServers, IEnumerable<string> topicNames, int partitions)
    {
        using var adminClient = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers }).Build();
        var specifications = topicNames.Select(name => new TopicSpecification
        {
            Name = name,
            NumPartitions = partitions,
            ReplicationFactor = 1
        });
        await adminClient.CreateTopicsAsync(specifications);
    }
}
