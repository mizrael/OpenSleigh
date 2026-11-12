using Confluent.Kafka;
using OpenSleigh.Benchmarks.Throughput.Infrastructure;
using OpenSleigh.Benchmarks.Throughput.Workload;

namespace OpenSleigh.Benchmarks.Throughput.Scenarios;

/// <summary>
/// Configuration experiments: the same workload at the library defaults and with settings an application
/// can change today, through the options the library exposes, the connection string, a registration
/// that replaces one of the library's, or a setting of the database itself.
/// Every variant is compared with the default of the same shape and size, measured in the same run.
/// </summary>
internal static class TuningCatalogue
{
    public const string Suite = "tuning";

    private const string MessagesPerSecond = "msg/s";
    private const string SagasPerSecond = "sagas/s";
    private const string Milliseconds = "ms";

    private static readonly string[] Tuning = [Suite];

    private const int StateCycles = 200;
    private const int AppendMessages = 800;
    private const int AppendCallers = 16;
    private const int RelayMessages = 2000;
    private const int SerialSagas = 400;
    private const int ConcurrentSagas = 1500;
    private const int KafkaConsumers = 4;

    private static readonly TimeSpan HandlerDelay = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan RelayCap = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ConsumeCap = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EndToEndCap = TimeSpan.FromSeconds(30);

    private static readonly SqlTuning PostgresAsyncCommit = new(SqlTuning.PostgresAsyncCommit);
    private static readonly SqlTuning PostgresAutoPrepare = new(SqlTuning.PostgresAutoPrepare);
    private static readonly SqlTuning PostgresDurableBest = new(SqlTuning.PostgresAutoPrepare + SqlTuning.PostgresNoReset, OutboxBatchSize: 100);
    private static readonly SqlTuning PostgresBest = new(SqlTuning.PostgresAsyncCommit + SqlTuning.PostgresAutoPrepare + SqlTuning.PostgresNoReset, OutboxBatchSize: 100);
    private static readonly SqlTuning SqlServerNoEncryption = new(SqlTuning.SqlServerNoEncryption);
    private static readonly SqlTuning SqlServerDelayedDurability = new(Statements: [SqlTuning.SqlServerDelayedDurability]);
    private static readonly SqlTuning SqlServerBest = new(
        SqlTuning.SqlServerNoEncryption,
        OutboxBatchSize: 100,
        Statements: [SqlTuning.SqlServerDelayedDurability, SqlTuning.SqlServerSnapshotReads]);

    private const string MongoJournaled = "journal=true";

    private static readonly Action<ProducerConfig> NoLinger = producer => producer.LingerMs = 0;

#if !RABBIT_CONSUMER_TUNING
    public static IReadOnlyList<Scenario> All { get; } = [];
#else
    public static IReadOnlyList<Scenario> All { get; } =
    [
        // ---- saga state: one find/lock/release cycle, the cost paid for every consumed message ----
        State("t-pg-state", "PostgreSQL, defaults", ctx => PersistenceTarget.Postgres(ctx)),
        State("t-pg-state-asynccommit", "PostgreSQL, synchronous_commit=off on the connection", ctx => PersistenceTarget.Postgres(ctx, PostgresAsyncCommit)),
        State("t-pg-state-autoprepare", "PostgreSQL, Max Auto Prepare=50", ctx => PersistenceTarget.Postgres(ctx, PostgresAutoPrepare)),
        State("t-pg-state-durablebest", "PostgreSQL, Max Auto Prepare=50, No Reset On Close", ctx => PersistenceTarget.Postgres(ctx, PostgresDurableBest)),
        State("t-mssql-state", "SQL Server, defaults", ctx => PersistenceTarget.SqlServer(ctx)),
        State("t-mssql-state-delayed", "SQL Server, DELAYED_DURABILITY = FORCED on the database", ctx => PersistenceTarget.SqlServer(ctx, SqlServerDelayedDurability)),
        State("t-mssql-state-noencrypt", "SQL Server, Encrypt=False", ctx => PersistenceTarget.SqlServer(ctx, SqlServerNoEncryption)),
        State("t-mongo-state", "MongoDB, defaults", ctx => PersistenceTarget.Mongo(ctx)),
        State("t-mongo-state-journaled", "MongoDB, journal=true", ctx => PersistenceTarget.Mongo(ctx, MongoJournaled)),

        // ---- outbox append: IMessageBus.PublishAsync from concurrent callers ----
        Append("t-pg-append", "PostgreSQL, defaults", ctx => PersistenceTarget.Postgres(ctx)),
        Append("t-pg-append-asynccommit", "PostgreSQL, synchronous_commit=off", ctx => PersistenceTarget.Postgres(ctx, PostgresAsyncCommit)),
        Append("t-mssql-append", "SQL Server, defaults", ctx => PersistenceTarget.SqlServer(ctx)),
        Append("t-mssql-append-delayed", "SQL Server, delayed durability", ctx => PersistenceTarget.SqlServer(ctx, SqlServerDelayedDurability)),
        Append("t-mongo-append", "MongoDB, defaults", ctx => PersistenceTarget.Mongo(ctx)),
        Append("t-mongo-append-journaled", "MongoDB, journal=true", ctx => PersistenceTarget.Mongo(ctx, MongoJournaled)),

        // ---- outbox relay to a publisher that does nothing ----
        Relay("t-pg-relay-b10", "PostgreSQL, 10 rows per read (default)", ctx => PersistenceTarget.Postgres(ctx)),
        Relay("t-pg-relay-b100", "PostgreSQL, 100 rows per read", ctx => PersistenceTarget.Postgres(ctx, new SqlTuning(OutboxBatchSize: 100))),
        Relay("t-pg-relay-b500", "PostgreSQL, 500 rows per read", ctx => PersistenceTarget.Postgres(ctx, new SqlTuning(OutboxBatchSize: 500))),
        Relay("t-pg-relay-b100-asynccommit", "PostgreSQL, 100 rows per read, synchronous_commit=off", ctx => PersistenceTarget.Postgres(ctx, new SqlTuning(SqlTuning.PostgresAsyncCommit, OutboxBatchSize: 100))),
        Relay("t-mssql-relay-b10", "SQL Server, 10 rows per read (default)", ctx => PersistenceTarget.SqlServer(ctx)),
        Relay("t-mssql-relay-b100", "SQL Server, 100 rows per read", ctx => PersistenceTarget.SqlServer(ctx, new SqlTuning(OutboxBatchSize: 100))),
        Relay("t-mssql-relay-b500", "SQL Server, 500 rows per read", ctx => PersistenceTarget.SqlServer(ctx, new SqlTuning(OutboxBatchSize: 500))),
        Relay("t-mssql-relay-b100-delayed", "SQL Server, 100 rows per read, delayed durability", ctx => PersistenceTarget.SqlServer(ctx, new SqlTuning(OutboxBatchSize: 100, Statements: [SqlTuning.SqlServerDelayedDurability]))),
        Relay("t-mongo-relay", "MongoDB (no row limit)", ctx => PersistenceTarget.Mongo(ctx)),

        // ---- one caller publishing to the broker, one message at a time, as the relay does ----
        Publish("t-kafka-publish", "Kafka, defaults", 1000, ctx => TransportTarget.Kafka(ctx, typeof(Ping))),
        Publish("t-kafka-publish-linger0", "Kafka, linger.ms=0", 1000, ctx => TransportTarget.Kafka(ctx, NoLinger, 1, typeof(Ping))),
        Publish("t-kafka-publish-linger0-acks1", "Kafka, linger.ms=0, acks=1, idempotence off", 1000, ctx => TransportTarget.Kafka(ctx,
            producer =>
            {
                producer.LingerMs = 0;
                producer.Acks = Acks.Leader;
                producer.EnableIdempotence = false;
            }, 1, typeof(Ping))),
        Publish("t-rabbit-publish", "RabbitMQ, defaults (no publisher confirms)", 20000, ctx => TransportTarget.Rabbit(ctx)),
        Publish("t-rabbit-publish-confirms", "RabbitMQ, every publish waits for the broker's confirm", 5000, ctx => TransportTarget.RabbitConfirming(ctx, 1, 0)),

        // ---- consumers alone: handler awaits 10 ms, in-memory persistence ----
        RabbitConsume("t-rabbit-consume-c1", 1, 0, 400),
        RabbitConsume("t-rabbit-consume-c1-p64", 1, 64, 400),
        RabbitConsume("t-rabbit-consume-c4-p8", 4, 8, 1600),
        RabbitConsume("t-rabbit-consume-c16-p16", 16, 16, 4000),
        RabbitConsume("t-rabbit-consume-c16-p32", 16, 32, 4000),
        RabbitConsume("t-rabbit-consume-c16-p0", 16, 0, 4000),
        RabbitConsume("t-rabbit-consume-c64-p128", 64, 128, 8000),
        new("t-kafka-consume-1x", Tuning, MessagesPerSecond, true,
            "400 messages, handler awaits 10 ms, 1 consumer on 1 partition, linger.ms=0",
            ctx => PipelineScenarios.ConsumeAsync(ctx, TransportTarget.Kafka(ctx, NoLinger, 1, typeof(Ping)), 400, 32, HandlerDelay, ConsumeCap)),
        new("t-kafka-consume-4x", Tuning, MessagesPerSecond, true,
            "1600 messages, handler awaits 10 ms, 4 consumers on 4 partitions, linger.ms=0",
            ctx => PipelineScenarios.ConsumeAsync(ctx, TransportTarget.Kafka(ctx, NoLinger, KafkaConsumers, typeof(Ping)), 1600, 32, HandlerDelay, ConsumeCap)),

        // ---- end to end, PostgreSQL + RabbitMQ ----
        EndToEnd("t-pg-rabbit", "defaults", SerialSagas, ctx => PersistenceTarget.Postgres(ctx), ctx => TransportTarget.Rabbit(ctx)),
        EndToEnd("t-pg-rabbit-asynccommit", "synchronous_commit=off", SerialSagas, ctx => PersistenceTarget.Postgres(ctx, PostgresAsyncCommit), ctx => TransportTarget.Rabbit(ctx)),
        EndToEnd("t-pg-rabbit-confirms", "publisher confirms", SerialSagas, ctx => PersistenceTarget.Postgres(ctx), ctx => TransportTarget.RabbitConfirming(ctx, 1, 0)),
        EndToEnd("t-pg-rabbit-c16", "16 concurrent consumers, prefetch 32", ConcurrentSagas, ctx => PersistenceTarget.Postgres(ctx), ctx => TransportTarget.RabbitConcurrent(ctx, 16, 32)),
        EndToEnd("t-pg-rabbit-c16-b100", "16 concurrent consumers, prefetch 32, relay reads 100 rows", ConcurrentSagas, ctx => PersistenceTarget.Postgres(ctx, new SqlTuning(OutboxBatchSize: 100)), ctx => TransportTarget.RabbitConcurrent(ctx, 16, 32)),
        EndToEnd("t-pg-rabbit-c16-durablebest", "16 concurrent consumers, prefetch 32, relay reads 100 rows, auto prepare, no reset on close", ConcurrentSagas, ctx => PersistenceTarget.Postgres(ctx, PostgresDurableBest), ctx => TransportTarget.RabbitConcurrent(ctx, 16, 32)),
        EndToEnd("t-pg-rabbit-c16-best", "16 concurrent consumers, prefetch 32, relay reads 100 rows, auto prepare, no reset on close, synchronous_commit=off", ConcurrentSagas, ctx => PersistenceTarget.Postgres(ctx, PostgresBest), ctx => TransportTarget.RabbitConcurrent(ctx, 16, 32)),
        EndToEnd("t-pg-rabbit-c64-best", "64 concurrent consumers, prefetch 128, relay reads 100 rows, auto prepare, no reset on close, synchronous_commit=off", ConcurrentSagas, ctx => PersistenceTarget.Postgres(ctx, PostgresBest), ctx => TransportTarget.RabbitConcurrent(ctx, 64, 128)),
        EndToEnd("t-pg-rabbit-c16-best-confirms", "as t-pg-rabbit-c16-best, with publisher confirms", ConcurrentSagas, ctx => PersistenceTarget.Postgres(ctx, PostgresBest), ctx => TransportTarget.RabbitConfirming(ctx, 16, 32)),

        // ---- end to end, SQL Server + RabbitMQ ----
        EndToEnd("t-mssql-rabbit", "defaults", SerialSagas, ctx => PersistenceTarget.SqlServer(ctx), ctx => TransportTarget.Rabbit(ctx)),
        EndToEnd("t-mssql-rabbit-delayed", "delayed durability", SerialSagas, ctx => PersistenceTarget.SqlServer(ctx, SqlServerDelayedDurability), ctx => TransportTarget.Rabbit(ctx)),
        EndToEnd("t-mssql-rabbit-c16", "16 concurrent consumers, prefetch 32", ConcurrentSagas, ctx => PersistenceTarget.SqlServer(ctx), ctx => TransportTarget.RabbitConcurrent(ctx, 16, 32)),
        EndToEnd("t-mssql-rabbit-c16-b100", "16 concurrent consumers, prefetch 32, relay reads 100 rows", ConcurrentSagas, ctx => PersistenceTarget.SqlServer(ctx, new SqlTuning(OutboxBatchSize: 100)), ctx => TransportTarget.RabbitConcurrent(ctx, 16, 32)),
        EndToEnd("t-mssql-rabbit-c16-best", "16 concurrent consumers, prefetch 32, relay reads 100 rows, delayed durability, snapshot reads, Encrypt=False", ConcurrentSagas, ctx => PersistenceTarget.SqlServer(ctx, SqlServerBest), ctx => TransportTarget.RabbitConcurrent(ctx, 16, 32)),

        // ---- end to end, MongoDB + RabbitMQ ----
        EndToEnd("t-mongo-rabbit", "defaults", SerialSagas, ctx => PersistenceTarget.Mongo(ctx), ctx => TransportTarget.Rabbit(ctx)),
        EndToEnd("t-mongo-rabbit-journaled", "journal=true", SerialSagas, ctx => PersistenceTarget.Mongo(ctx, MongoJournaled), ctx => TransportTarget.Rabbit(ctx)),
        EndToEnd("t-mongo-rabbit-c16", "16 concurrent consumers, prefetch 32", ConcurrentSagas, ctx => PersistenceTarget.Mongo(ctx), ctx => TransportTarget.RabbitConcurrent(ctx, 16, 32)),
        EndToEnd("t-mongo-rabbit-c64", "64 concurrent consumers, prefetch 128", ConcurrentSagas, ctx => PersistenceTarget.Mongo(ctx), ctx => TransportTarget.RabbitConcurrent(ctx, 64, 128)),
        EndToEnd("t-mongo-rabbit-c16-journaled", "16 concurrent consumers, prefetch 32, journal=true", ConcurrentSagas, ctx => PersistenceTarget.Mongo(ctx, MongoJournaled), ctx => TransportTarget.RabbitConcurrent(ctx, 16, 32)),

        // ---- end to end, Kafka ----
        EndToEnd("t-pg-kafka", "defaults", SerialSagas, ctx => PersistenceTarget.Postgres(ctx), ctx => KafkaSaga(ctx, null, 1)),
        EndToEnd("t-pg-kafka-linger0", "linger.ms=0", SerialSagas, ctx => PersistenceTarget.Postgres(ctx), ctx => KafkaSaga(ctx, NoLinger, 1)),
        EndToEnd("t-pg-kafka-linger0-4x", "linger.ms=0, 4 consumers on 4 partitions", ConcurrentSagas, ctx => PersistenceTarget.Postgres(ctx), ctx => KafkaSaga(ctx, NoLinger, KafkaConsumers)),
        EndToEnd("t-pg-kafka-best", "linger.ms=0, 4 consumers on 4 partitions, relay reads 100 rows, synchronous_commit=off, auto prepare", ConcurrentSagas, ctx => PersistenceTarget.Postgres(ctx, PostgresBest), ctx => KafkaSaga(ctx, NoLinger, KafkaConsumers)),
        EndToEnd("t-mssql-kafka", "defaults", SerialSagas, ctx => PersistenceTarget.SqlServer(ctx), ctx => KafkaSaga(ctx, null, 1)),
        EndToEnd("t-mssql-kafka-linger0", "linger.ms=0", SerialSagas, ctx => PersistenceTarget.SqlServer(ctx), ctx => KafkaSaga(ctx, NoLinger, 1)),
        EndToEnd("t-mssql-kafka-linger0-delayed", "linger.ms=0, delayed durability", SerialSagas, ctx => PersistenceTarget.SqlServer(ctx, SqlServerDelayedDurability), ctx => KafkaSaga(ctx, NoLinger, 1)),
        EndToEnd("t-mssql-kafka-best", "linger.ms=0, 4 consumers on 4 partitions, relay reads 100 rows, delayed durability, snapshot reads, Encrypt=False", ConcurrentSagas, ctx => PersistenceTarget.SqlServer(ctx, SqlServerBest), ctx => KafkaSaga(ctx, NoLinger, KafkaConsumers)),
        EndToEnd("t-mongo-kafka", "defaults", SerialSagas, ctx => PersistenceTarget.Mongo(ctx), ctx => KafkaSaga(ctx, null, 1)),
        EndToEnd("t-mongo-kafka-linger0", "linger.ms=0", SerialSagas, ctx => PersistenceTarget.Mongo(ctx), ctx => KafkaSaga(ctx, NoLinger, 1)),
        EndToEnd("t-mongo-kafka-best", "linger.ms=0, 4 consumers on 4 partitions", ConcurrentSagas, ctx => PersistenceTarget.Mongo(ctx), ctx => KafkaSaga(ctx, NoLinger, KafkaConsumers)),
    ];

    private static TransportTarget KafkaSaga(ScenarioContext context, Action<ProducerConfig>? tuneProducer, int consumers)
        => TransportTarget.Kafka(context, tuneProducer, consumers, typeof(StartBench), typeof(BenchStep));

    private static Scenario State(string name, string configuration, Func<ScenarioContext, PersistenceTarget> persistence)
        => new(name, Tuning, Milliseconds, false,
            $"find/lock/release cycle on a new saga; mean of {StateCycles} cycles. {configuration}",
            ctx => SagaStateScenarios.LongSagaCycleAsync(ctx, persistence(ctx), 0, StateCycles));

    private static Scenario Append(string name, string configuration, Func<ScenarioContext, PersistenceTarget> persistence)
        => new(name, Tuning, MessagesPerSecond, true,
            $"{AppendMessages} IMessageBus.PublishAsync calls from {AppendCallers} concurrent callers. {configuration}",
            ctx => PipelineScenarios.OutboxAppendAsync(ctx, persistence(ctx), AppendMessages, AppendCallers));

    private static Scenario Relay(string name, string configuration, Func<ScenarioContext, PersistenceTarget> persistence)
        => new(name, Tuning, MessagesPerSecond, true,
            $"{RelayMessages} pending outbox messages relayed to a no-op publisher. {configuration}",
            ctx => PipelineScenarios.OutboxRelayAsync(ctx, persistence(ctx), RelayMessages, RelayCap));

    private static Scenario Publish(string name, string configuration, int messages, Func<ScenarioContext, TransportTarget> transport)
        => new(name, Tuning, MessagesPerSecond, true,
            $"{messages} messages published by one caller, each awaited before the next. {configuration}",
            ctx => PipelineScenarios.PublishSeriallyAsync(ctx, transport(ctx), messages));

    private static Scenario RabbitConsume(string name, ushort concurrency, ushort prefetchCount, int messages)
        => new(name, Tuning, MessagesPerSecond, true,
            $"{messages} messages consumed from RabbitMQ, handler awaits 10 ms, in-memory persistence. ConsumerDispatchConcurrency {concurrency}, PrefetchCount {prefetchCount}",
            ctx => PipelineScenarios.ConsumeAsync(ctx, TransportTarget.RabbitConcurrent(ctx, concurrency, prefetchCount), messages, 1, HandlerDelay, ConsumeCap));

    private static Scenario EndToEnd(
        string name,
        string configuration,
        int sagas,
        Func<ScenarioContext, PersistenceTarget> persistence,
        Func<ScenarioContext, TransportTarget> transport)
        => new(name, Tuning, SagasPerSecond, true,
            $"{sagas} two-hop sagas. {configuration}",
            ctx => PipelineScenarios.EndToEndAsync(ctx, persistence(ctx), transport(ctx), sagas, EndToEndCap));
#endif
}
