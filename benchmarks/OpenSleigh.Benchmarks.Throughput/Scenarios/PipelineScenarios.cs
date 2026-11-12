using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenSleigh.Benchmarks.Throughput.Infrastructure;
using OpenSleigh.Benchmarks.Throughput.Workload;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Outbox;
using OpenSleigh.Transport;
using System.Diagnostics;

namespace OpenSleigh.Benchmarks.Throughput.Scenarios;

/// <summary>
/// Scenarios that drive the message pipeline: outbox append, outbox relay, consumers and whole sagas.
/// </summary>
internal static class PipelineScenarios
{
    private static readonly TimeSpan WarmUpTimeout = TimeSpan.FromSeconds(90);
    private const int PublishParallelism = 8;

    /// <summary>
    /// IMessageBus.PublishAsync from concurrent callers, one DI scope per call, relay not running.
    /// </summary>
    public static async Task<double> OutboxAppendAsync(ScenarioContext context, PersistenceTarget persistence, int messages, int parallelism)
    {
        var host = BenchHost.Build(context, cfg =>
        {
            persistence.Configure(cfg);
            cfg.AddSaga<BenchSaga, BenchState>();
        });

        try
        {
            persistence.Prepare(host.Services);
            await PublishStartAsync(host.Services);

            var stopwatch = Stopwatch.StartNew();
            await BenchHost.RunConcurrentlyAsync(messages, parallelism, _ => PublishStartAsync(host.Services));
            stopwatch.Stop();

            return messages / stopwatch.Elapsed.TotalSeconds;
        }
        finally
        {
            await BenchHost.StopAndDisposeAsync(host, persistence.DropAsync);
        }
    }

    /// <summary>
    /// Pre-filled outbox drained by the real OutboxBackgroundService into a publisher that does nothing.
    /// </summary>
    public static async Task<double> OutboxRelayAsync(
        ScenarioContext context,
        PersistenceTarget persistence,
        int messages,
        TimeSpan cap)
    {
        var host = BenchHost.Build(context, cfg =>
        {
            persistence.Configure(cfg);
            cfg.AddSaga<BenchSaga, BenchState>();
        }, services => services.AddSingleton<IPublisher, CountingPublisher>());

        try
        {
            persistence.Prepare(host.Services);
            await SeedOutboxAsync(host.Services, messages);

            var tracker = host.Services.GetRequiredService<WorkTracker>();
            var start = Stopwatch.GetTimestamp();
            await host.StartAsync();
            await BenchHost.WaitUntilAsync(() => tracker.Count >= messages, cap);

            return tracker.RateSince(start);
        }
        finally
        {
            await BenchHost.StopAndDisposeAsync(host, persistence.DropAsync);
        }
    }

    /// <summary>
    /// Messages pushed straight to the broker and consumed by the real subscriber into a one-message saga
    /// on the in-memory persistence, so the consumer is the only real infrastructure in the path.
    /// </summary>
    public static async Task<double> ConsumeAsync(
        ScenarioContext context,
        TransportTarget transport,
        int messages,
        int publishParallelism,
        TimeSpan handlerDelay,
        TimeSpan cap)
    {
        await transport.PrepareAsync();

        var host = BenchHost.Build(context, cfg =>
        {
            PersistenceTarget.InMemory(context).Configure(cfg);
            transport.Configure(cfg);
            cfg.AddSaga<PingSaga>();
        });

        try
        {
            var tracker = host.Services.GetRequiredService<WorkTracker>();
            var publisher = host.Services.GetRequiredService<IPublisher>();
            var systemInfo = host.Services.GetRequiredService<ISystemInfo>();

            await host.StartAsync();

            await publisher.PublishAsync(NewPing(systemInfo));
            if (!await BenchHost.WaitUntilAsync(() => tracker.Count >= 1, WarmUpTimeout))
                throw new TimeoutException("warm-up message was not consumed");
            await Task.Delay(transport.SettleTime);

            tracker.Reset();
            tracker.HandlerDelay = handlerDelay;

            var start = Stopwatch.GetTimestamp();
            await BenchHost.RunConcurrentlyAsync(messages, publishParallelism,
                async _ => await publisher.PublishAsync(NewPing(systemInfo)));
            await BenchHost.WaitUntilAsync(() => tracker.Count >= messages, cap - Stopwatch.GetElapsedTime(start));

            return tracker.RateBetweenFirstAndLast();
        }
        finally
        {
            await BenchHost.StopAndDisposeAsync(host);
        }
    }

    /// <summary>
    /// One caller publishing straight to the broker, each message awaited before the next,
    /// the way the outbox relay publishes. Nothing consumes.
    /// </summary>
    public static async Task<double> PublishSeriallyAsync(ScenarioContext context, TransportTarget transport, int messages)
    {
        const int warmUpMessages = 20;

        await transport.PrepareAsync();

        var host = BenchHost.Build(context, cfg =>
        {
            PersistenceTarget.InMemory(context).Configure(cfg);
            transport.Configure(cfg);
            cfg.AddSaga<PingSaga>();
        });

        try
        {
            var publisher = host.Services.GetRequiredService<IPublisher>();
            var systemInfo = host.Services.GetRequiredService<ISystemInfo>();

            for (var i = 0; i < warmUpMessages; i++)
                await publisher.PublishAsync(NewPing(systemInfo));

            var stopwatch = Stopwatch.StartNew();
            for (var i = 0; i < messages; i++)
                await publisher.PublishAsync(NewPing(systemInfo));
            stopwatch.Stop();

            return messages / stopwatch.Elapsed.TotalSeconds;
        }
        finally
        {
            await BenchHost.StopAndDisposeAsync(host);
        }
    }

    /// <summary>
    /// Whole two-hop sagas: publish, outbox, relay, broker, consumer, saga state, and back again for the second hop.
    /// </summary>
    public static async Task<double> EndToEndAsync(
        ScenarioContext context,
        PersistenceTarget persistence,
        TransportTarget transport,
        int sagas,
        TimeSpan cap)
    {
        await transport.PrepareAsync();

        var host = BenchHost.Build(context, cfg =>
        {
            persistence.Configure(cfg);
            transport.Configure(cfg);
            cfg.AddSaga<BenchSaga, BenchState>();
        });

        try
        {
            var tracker = host.Services.GetRequiredService<WorkTracker>();
            await StartAndWarmUpAsync(host, persistence, tracker);
            await Task.Delay(transport.SettleTime);

            tracker.Reset();

            var start = Stopwatch.GetTimestamp();
            await BenchHost.RunConcurrentlyAsync(sagas, PublishParallelism, _ => PublishStartAsync(host.Services));
            await BenchHost.WaitUntilAsync(() => tracker.Count >= sagas, cap - Stopwatch.GetElapsedTime(start));

            return tracker.RateSince(start);
        }
        finally
        {
            await BenchHost.StopAndDisposeAsync(host, persistence.DropAsync);
        }
    }

    /// <summary>
    /// One saga at a time, started at an arbitrary point of the relay's polling cycle: milliseconds until it completes.
    /// </summary>
    public static async Task<double> EndToEndLatencyAsync(
        ScenarioContext context,
        PersistenceTarget persistence,
        TransportTarget transport,
        int samples)
    {
        await transport.PrepareAsync();

        var host = BenchHost.Build(context, cfg =>
        {
            persistence.Configure(cfg);
            transport.Configure(cfg);
            cfg.AddSaga<BenchSaga, BenchState>();
        });

        try
        {
            var tracker = host.Services.GetRequiredService<WorkTracker>();
            await StartAndWarmUpAsync(host, persistence, tracker);

            var latencies = new List<double>(samples);
            for (var i = 0; i < samples; i++)
            {
                tracker.Reset();
                await Task.Delay(Random.Shared.Next(100, 1100));

                var stopwatch = Stopwatch.StartNew();
                await PublishStartAsync(host.Services);
                if (!await BenchHost.WaitUntilAsync(() => tracker.Count >= 1, WarmUpTimeout))
                    throw new TimeoutException("saga did not complete");
                latencies.Add(stopwatch.Elapsed.TotalMilliseconds);
            }

            return Statistics.Median(latencies);
        }
        finally
        {
            await BenchHost.StopAndDisposeAsync(host, persistence.DropAsync);
        }
    }

    private static async Task StartAndWarmUpAsync(IHost host, PersistenceTarget persistence, WorkTracker tracker)
    {
        persistence.Prepare(host.Services);
        await host.StartAsync();

        await PublishStartAsync(host.Services);
        if (!await BenchHost.WaitUntilAsync(() => tracker.Count >= 1, WarmUpTimeout))
            throw new TimeoutException("warm-up saga did not complete");
    }

    private static async Task PublishStartAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await bus.PublishAsync(new StartBench { CorrelationId = Guid.NewGuid().ToString() });
    }

    private static MessageEnvelope NewPing(ISystemInfo systemInfo)
        => MessageEnvelope.Create(new Ping { CorrelationId = Guid.NewGuid().ToString() }, systemInfo);

    private static async Task SeedOutboxAsync(IServiceProvider services, int messages)
    {
        const int chunkSize = 50;
        var systemInfo = services.GetRequiredService<ISystemInfo>();

        for (var seeded = 0; seeded < messages; seeded += chunkSize)
        {
            using var scope = services.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
            var chunk = Enumerable.Range(0, Math.Min(chunkSize, messages - seeded))
                .Select(_ => MessageEnvelope.Create(new StartBench { CorrelationId = Guid.NewGuid().ToString() }, systemInfo))
                .ToArray();
            await repository.AppendAsync(chunk);
        }
    }
}
