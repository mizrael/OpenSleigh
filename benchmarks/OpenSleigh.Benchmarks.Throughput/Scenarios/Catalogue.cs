using OpenSleigh.Benchmarks.Throughput.Infrastructure;
using OpenSleigh.Benchmarks.Throughput.Workload;

namespace OpenSleigh.Benchmarks.Throughput.Scenarios;

internal static class Catalogue
{
    private const string MessagesPerSecond = "msg/s";
    private const string SagasPerSecond = "sagas/s";
    private const string Milliseconds = "ms";

    private static readonly string[] LocalOnly = [Suites.Local];
    private static readonly string[] GateAndFull = [Suites.Gate, Suites.Full];
    private static readonly string[] FullOnly = [Suites.Full];

    private const int SqlDefaultBatchSize = 10;

    public static IReadOnlyList<Scenario> All { get; } = [.. Build(), .. TuningCatalogue.All];

    private static List<Scenario> Build() =>
    [
        // ---- no infrastructure ----
        new("local-outbox-relay", LocalOnly, MessagesPerSecond, true,
            "2000 pending messages, fake outbox returning 10 per read, no-op publisher; rate until drained or 30 s",
            ctx => PipelineScenarios.OutboxRelayAsync(ctx, LocalScenarios.FakeBatchedOutbox(SqlDefaultBatchSize), 2000, TimeSpan.FromSeconds(30))),

        new("local-kafka-consumer", LocalOnly, MessagesPerSecond, true,
            "1000 messages always ready in a fake consumer, real subscriber/handler/saga pipeline, in-memory persistence; rate until consumed or 15 s",
            ctx => LocalScenarios.KafkaConsumerLoopAsync(ctx, 1000, TimeSpan.FromSeconds(15))),

        new("local-inmemory-e2e", LocalOnly, SagasPerSecond, true,
            "2000 two-hop sagas, in-memory transport and persistence; rate until completed or 30 s",
            ctx => PipelineScenarios.EndToEndAsync(ctx, PersistenceTarget.InMemory(ctx), TransportTarget.InMemory(), 2000, TimeSpan.FromSeconds(30))),

        new("local-inmemory-latency", LocalOnly, Milliseconds, false,
            "one two-hop saga at a time, in-memory transport and persistence; median of 5",
            ctx => PipelineScenarios.EndToEndLatencyAsync(ctx, PersistenceTarget.InMemory(ctx), TransportTarget.InMemory(), 5)),

        new("local-sqlite-dbcontext", LocalOnly, Milliseconds, false,
            "construct and dispose 2000 SagaDbContext on a SQLite file; mean per context",
            ctx => DbContextScenarios.ConstructOnSqliteAsync(ctx, 2000)),

        new("local-sqlite-state-longsaga", LocalOnly, Milliseconds, false,
            "find/lock/release cycle on a saga with 300 processed messages, SQL repository on a SQLite file; mean of 40 cycles",
            ctx => SagaStateScenarios.LongSagaCycleAsync(ctx, PersistenceTarget.SqliteSagaState(ctx), 300, 40)),

        // ---- real infrastructure ----
        new("sqlserver-dbcontext", GateAndFull, Milliseconds, false,
            "resolve 200 SagaDbContext from new DI scopes; mean per context",
            ctx => DbContextScenarios.ResolveFromScopeAsync(ctx, PersistenceTarget.SqlServer(ctx), 200)),

        new("postgres-dbcontext", GateAndFull, Milliseconds, false,
            "resolve 200 SagaDbContext from new DI scopes; mean per context",
            ctx => DbContextScenarios.ResolveFromScopeAsync(ctx, PersistenceTarget.Postgres(ctx), 200)),

        new("sqlserver-outbox-append", GateAndFull, MessagesPerSecond, true,
            "400 IMessageBus.PublishAsync calls from 16 concurrent callers, one scope each",
            ctx => PipelineScenarios.OutboxAppendAsync(ctx, PersistenceTarget.SqlServer(ctx), 400, 16)),

        new("postgres-outbox-append", GateAndFull, MessagesPerSecond, true,
            "400 IMessageBus.PublishAsync calls from 16 concurrent callers, one scope each",
            ctx => PipelineScenarios.OutboxAppendAsync(ctx, PersistenceTarget.Postgres(ctx), 400, 16)),

        new("mongo-outbox-append", FullOnly, MessagesPerSecond, true,
            "400 IMessageBus.PublishAsync calls from 16 concurrent callers, one scope each",
            ctx => PipelineScenarios.OutboxAppendAsync(ctx, PersistenceTarget.Mongo(ctx), 400, 16)),

        new("sqlserver-outbox-relay", GateAndFull, MessagesPerSecond, true,
            "500 pending outbox rows relayed to a no-op publisher; rate until drained or 10 s",
            ctx => PipelineScenarios.OutboxRelayAsync(ctx, PersistenceTarget.SqlServer(ctx), 500, TimeSpan.FromSeconds(10))),

        new("postgres-outbox-relay", FullOnly, MessagesPerSecond, true,
            "500 pending outbox rows relayed to a no-op publisher; rate until drained or 10 s",
            ctx => PipelineScenarios.OutboxRelayAsync(ctx, PersistenceTarget.Postgres(ctx), 500, TimeSpan.FromSeconds(10))),

        new("mongo-outbox-relay", FullOnly, MessagesPerSecond, true,
            "500 pending outbox documents relayed to a no-op publisher; rate until drained or 10 s",
            ctx => PipelineScenarios.OutboxRelayAsync(ctx, PersistenceTarget.Mongo(ctx), 500, TimeSpan.FromSeconds(10))),

        new("postgres-state-longsaga", GateAndFull, Milliseconds, false,
            "find/lock/release cycle on a saga with 300 processed messages; mean of 40 cycles",
            ctx => SagaStateScenarios.LongSagaCycleAsync(ctx, PersistenceTarget.Postgres(ctx), 300, 40)),

        new("sqlserver-state-longsaga", FullOnly, Milliseconds, false,
            "find/lock/release cycle on a saga with 300 processed messages; mean of 40 cycles",
            ctx => SagaStateScenarios.LongSagaCycleAsync(ctx, PersistenceTarget.SqlServer(ctx), 300, 40)),

        new("mongo-state-longsaga", FullOnly, Milliseconds, false,
            "find/lock/release cycle on a saga with 300 processed messages; mean of 40 cycles",
            ctx => SagaStateScenarios.LongSagaCycleAsync(ctx, PersistenceTarget.Mongo(ctx), 300, 40)),

        new("mongo-state-manysagas", GateAndFull, Milliseconds, false,
            "find/lock/release cycle with 20000 other sagas in the collection; mean of 40 cycles",
            ctx => SagaStateScenarios.MongoManySagasCycleAsync(ctx, 20000, 40)),

        new("rabbit-consume", GateAndFull, MessagesPerSecond, true,
            "400 messages consumed from RabbitMQ, handler awaits 10 ms, in-memory persistence; rate until consumed or 10 s",
            ctx => PipelineScenarios.ConsumeAsync(ctx, TransportTarget.Rabbit(ctx), 400, 1, TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(10))),

        new("kafka-consume", GateAndFull, MessagesPerSecond, true,
            "400 messages consumed from Kafka, in-memory persistence; rate until consumed or 10 s",
            ctx => PipelineScenarios.ConsumeAsync(ctx, TransportTarget.Kafka(ctx, typeof(Ping)), 400, 32, TimeSpan.Zero, TimeSpan.FromSeconds(10))),

        new("postgres-rabbit-e2e", GateAndFull, SagasPerSecond, true,
            "200 two-hop sagas on PostgreSQL + RabbitMQ; rate until completed or 15 s",
            ctx => PipelineScenarios.EndToEndAsync(ctx, PersistenceTarget.Postgres(ctx), TransportTarget.Rabbit(ctx), 200, TimeSpan.FromSeconds(15))),

        new("mongo-rabbit-e2e", GateAndFull, SagasPerSecond, true,
            "200 two-hop sagas on MongoDB + RabbitMQ; rate until completed or 15 s",
            ctx => PipelineScenarios.EndToEndAsync(ctx, PersistenceTarget.Mongo(ctx), TransportTarget.Rabbit(ctx), 200, TimeSpan.FromSeconds(15))),

        new("sqlserver-rabbit-e2e", FullOnly, SagasPerSecond, true,
            "200 two-hop sagas on SQL Server + RabbitMQ; rate until completed or 15 s",
            ctx => PipelineScenarios.EndToEndAsync(ctx, PersistenceTarget.SqlServer(ctx), TransportTarget.Rabbit(ctx), 200, TimeSpan.FromSeconds(15))),

        new("postgres-kafka-e2e", FullOnly, SagasPerSecond, true,
            "200 two-hop sagas on PostgreSQL + Kafka; rate until completed or 15 s",
            ctx => PipelineScenarios.EndToEndAsync(ctx, PersistenceTarget.Postgres(ctx), TransportTarget.Kafka(ctx, typeof(StartBench), typeof(BenchStep)), 200, TimeSpan.FromSeconds(15))),

        new("postgres-rabbit-latency", FullOnly, Milliseconds, false,
            "one two-hop saga at a time on PostgreSQL + RabbitMQ; median of 5",
            ctx => PipelineScenarios.EndToEndLatencyAsync(ctx, PersistenceTarget.Postgres(ctx), TransportTarget.Rabbit(ctx), 5)),

#if RABBIT_CONSUMER_TUNING
        new("rabbit-consume-concurrent", GateAndFull, MessagesPerSecond, true,
            "as rabbit-consume, with ConsumerDispatchConcurrency = 16 and PrefetchCount = 64",
            ctx => PipelineScenarios.ConsumeAsync(ctx, TransportTarget.RabbitConcurrent(ctx, 16, 64), 400, 1, TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(10))),

        new("postgres-rabbit-e2e-concurrent", FullOnly, SagasPerSecond, true,
            "as postgres-rabbit-e2e, with ConsumerDispatchConcurrency = 16 and PrefetchCount = 64",
            ctx => PipelineScenarios.EndToEndAsync(ctx, PersistenceTarget.Postgres(ctx), TransportTarget.RabbitConcurrent(ctx, 16, 64), 200, TimeSpan.FromSeconds(15))),
#endif
    ];

    public static IReadOnlyList<Scenario> Select(string? suite, string? names)
    {
        if (!string.IsNullOrWhiteSpace(names))
        {
            var wanted = names.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var unknown = wanted.Where(name => All.All(s => s.Name != name)).ToArray();
            if (unknown.Length > 0)
                throw new ArgumentException($"unknown scenario(s): {string.Join(", ", unknown)}");
            return All.Where(s => wanted.Contains(s.Name)).ToArray();
        }

        var selected = All.Where(s => s.Suites.Contains(suite ?? Suites.Local)).ToArray();
        if (selected.Length == 0)
            throw new ArgumentException($"unknown or empty suite '{suite}'");
        return selected;
    }
}
