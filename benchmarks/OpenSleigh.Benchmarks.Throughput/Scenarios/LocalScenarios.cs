using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OpenSleigh.Benchmarks.Throughput.Infrastructure;
using OpenSleigh.Benchmarks.Throughput.Workload;
using OpenSleigh.Outbox;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Transport;
using OpenSleigh.Transport.Kafka;
using OpenSleigh.Utils;
using System.Diagnostics;

namespace OpenSleigh.Benchmarks.Throughput.Scenarios;

/// <summary>
/// Scenarios about creating SagaDbContext instances, which the SQL providers do several times per message.
/// </summary>
internal static class DbContextScenarios
{
    /// <summary>
    /// Milliseconds to resolve one SagaDbContext from a new DI scope, on an existing database.
    /// </summary>
    public static async Task<double> ResolveFromScopeAsync(ScenarioContext context, PersistenceTarget persistence, int iterations)
    {
        var host = BenchHost.Build(context, persistence.Configure);

        try
        {
            ResolveOnce(host.Services);

            var stopwatch = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
                ResolveOnce(host.Services);
            stopwatch.Stop();

            return stopwatch.Elapsed.TotalMilliseconds / iterations;
        }
        finally
        {
            await BenchHost.StopAndDisposeAsync(host, persistence.DropAsync);
        }
    }

    /// <summary>
    /// Milliseconds to construct one SagaDbContext on a local SQLite file: no server, no network.
    /// </summary>
    public static Task<double> ConstructOnSqliteAsync(ScenarioContext context, int iterations)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"osb_{context.RunId}.db");
        var options = new DbContextOptionsBuilder<SagaDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;

        try
        {
            using (var warmUp = new SagaDbContext(options))
                warmUp.Database.EnsureCreated();

            var stopwatch = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
            {
                using var dbContext = new SagaDbContext(options);
            }
            stopwatch.Stop();

            return Task.FromResult(stopwatch.Elapsed.TotalMilliseconds / iterations);
        }
        finally
        {
            try
            {
                File.Delete(databasePath);
            }
            catch
            {
            }
        }
    }

    private static void ResolveOnce(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<SagaDbContext>();
    }
}

/// <summary>
/// Scenarios that need no infrastructure: the library loops run against fakes or the in-memory providers.
/// </summary>
internal static class LocalScenarios
{
    /// <summary>
    /// The real Kafka subscriber, handler, parser and saga pipeline, fed by a fake consumer that always has a message ready.
    /// </summary>
    public static async Task<double> KafkaConsumerLoopAsync(ScenarioContext context, int messages, TimeSpan cap)
    {
        var feed = new FakeKafkaFeed();
        var kafkaConfiguration = new KafkaConfiguration("localhost:9092");

        var host = BenchHost.Build(context, cfg =>
        {
            PersistenceTarget.InMemory(context).Configure(cfg);
            cfg.UseKafkaTransport(kafkaConfiguration);
            cfg.AddSaga<PingSaga>();
        }, services =>
        {
            services.RemoveAll<IConsumerBuilderFactory>();
            services.RemoveAll<IKafkaPublisherExecutor>();
            services.RemoveAll<IPublisher>();
            services.AddSingleton<IConsumerBuilderFactory>(new FakeConsumerBuilderFactory(feed));
            services.AddSingleton<IKafkaPublisherExecutor, UnusedKafkaPublisher>();
            services.AddSingleton<IPublisher, CountingPublisher>();
        });

        try
        {
            var tracker = host.Services.GetRequiredService<WorkTracker>();
            var systemInfo = host.Services.GetRequiredService<ISystemInfo>();
            var serializer = host.Services.GetRequiredService<ISerializer>();

            for (var i = 0; i < messages; i++)
                feed.Enqueue(MessageEnvelope.Create(new Ping { CorrelationId = Guid.NewGuid().ToString() }, systemInfo), serializer);

            await host.StartAsync();
            await BenchHost.WaitUntilAsync(() => tracker.Count >= messages, cap);

            return tracker.RateBetweenFirstAndLast();
        }
        finally
        {
            await BenchHost.StopAndDisposeAsync(host);
        }
    }

    public static PersistenceTarget FakeBatchedOutbox(int batchSize)
        => new(
            "fake",
            cfg => cfg.Services.AddSingleton<IOutboxRepository>(new BatchedFakeOutbox(batchSize)),
            _ => Task.CompletedTask);
}
