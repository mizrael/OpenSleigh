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
    /// Pre-filled outbox drained by the real OutboxBackgroundService into a publisher that does nothing,
    /// or into the publisher of <paramref name="transport"/> when one is given. Nothing consumes what it publishes.
    /// </summary>
    public static Task<double> OutboxRelayAsync(
        ScenarioContext context,
        PersistenceTarget persistence,
        int messages,
        TimeSpan cap,
        TransportTarget? transport = null)
        => RunOutboxRelayAsync(context, persistence, messages, cap, transport,
            instrument: null,
            completed: (_, tracker, start) => tracker.RateSince(start));

    /// <summary>
    /// The same run as <see cref="OutboxRelayAsync"/>, reporting how long the relay spent in each call.
    /// </summary>
    public static Task<EndToEndProfile> ProfileOutboxRelayAsync(
        ScenarioContext context,
        PersistenceTarget persistence,
        int messages,
        TimeSpan cap,
        TransportTarget? transport = null)
        => RunOutboxRelayAsync(context, persistence, messages, cap, transport,
            instrument: StageTimings.Instrument,
            completed: (services, tracker, start) => new EndToEndProfile(
                tracker.Count,
                tracker.Count / Math.Max(tracker.RateSince(start), double.Epsilon) * 1000,
                services.GetRequiredService<StageTimings>().Snapshot()));

    private static async Task<T> RunOutboxRelayAsync<T>(
        ScenarioContext context,
        PersistenceTarget persistence,
        int messages,
        TimeSpan cap,
        TransportTarget? transport,
        Action<IServiceCollection>? instrument,
        Func<IServiceProvider, WorkTracker, long, T> completed)
    {
        if (transport is not null)
            await transport.PrepareAsync();

        var host = BenchHost.Build(context, cfg =>
        {
            persistence.Configure(cfg);
            transport?.Configure(cfg);
            cfg.AddSaga<BenchSaga, BenchState>();
            cfg.SetPublishOnly();
        }, services =>
        {
            if (transport is null)
                services.AddSingleton<IPublisher, CountingPublisher>();
            else
                services.TryDecorate<IPublisher>((provider, inner)
                    => new TrackingPublisher(inner, provider.GetRequiredService<WorkTracker>()));
            instrument?.Invoke(services);
        });

        try
        {
            var tracker = host.Services.GetRequiredService<WorkTracker>();

            // the first publish declares exchanges and queues
            var systemInfo = host.Services.GetRequiredService<ISystemInfo>();
            await host.Services.GetRequiredService<IPublisher>().PublishAsync(NewStart(systemInfo));
            tracker.Reset();

            await SeedOutboxAsync(host.Services, messages);
            host.Services.GetService<StageTimings>()?.Reset();

            var start = Stopwatch.GetTimestamp();
            await host.StartAsync();
            await BenchHost.WaitUntilAsync(() => tracker.Count >= messages, cap);

            return completed(host.Services, tracker, start);
        }
        finally
        {
            await BenchHost.StopAndDisposeAsync(host, persistence.DropAsync);
        }
    }

    /// <summary>
    /// Messages handed to the transport's publisher one after the other, the way the relay does. Nothing consumes them.
    /// </summary>
    public static async Task<double> PublishAsync(ScenarioContext context, TransportTarget transport, int messages)
    {
        await transport.PrepareAsync();

        var host = BenchHost.Build(context, cfg =>
        {
            PersistenceTarget.InMemory(context).Configure(cfg);
            transport.Configure(cfg);
            cfg.AddSaga<PingSaga>();
            cfg.SetPublishOnly();
        });

        try
        {
            var publisher = host.Services.GetRequiredService<IPublisher>();
            var systemInfo = host.Services.GetRequiredService<ISystemInfo>();

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
    /// Whole two-hop sagas: publish, outbox, relay, broker, consumer, saga state, and back again for the second hop.
    /// </summary>
    public static Task<double> EndToEndAsync(
        ScenarioContext context,
        PersistenceTarget persistence,
        TransportTarget transport,
        int sagas,
        TimeSpan cap)
        => RunEndToEndAsync(context, persistence, transport, sagas, cap,
            instrument: null,
            warmedUp: _ => { },
            completed: (_, tracker, start) => tracker.RateSince(start));

    /// <summary>
    /// The same run as <see cref="EndToEndAsync"/>, reporting how long the pipeline spent in each call
    /// to its persistence and transport.
    /// </summary>
    public static Task<EndToEndProfile> ProfileEndToEndAsync(
        ScenarioContext context,
        PersistenceTarget persistence,
        TransportTarget transport,
        int sagas,
        TimeSpan cap)
        => RunEndToEndAsync(context, persistence, transport, sagas, cap,
            instrument: StageTimings.Instrument,
            warmedUp: services => services.GetRequiredService<StageTimings>().Reset(),
            completed: (services, tracker, start) => new EndToEndProfile(
                tracker.Count,
                tracker.Count / Math.Max(tracker.RateSince(start), double.Epsilon) * 1000,
                services.GetRequiredService<StageTimings>().Snapshot()));

    private static async Task<T> RunEndToEndAsync<T>(
        ScenarioContext context,
        PersistenceTarget persistence,
        TransportTarget transport,
        int sagas,
        TimeSpan cap,
        Action<IServiceCollection>? instrument,
        Action<IServiceProvider> warmedUp,
        Func<IServiceProvider, WorkTracker, long, T> completed)
    {
        await transport.PrepareAsync();

        var host = BenchHost.Build(context, cfg =>
        {
            persistence.Configure(cfg);
            transport.Configure(cfg);
            cfg.AddSaga<BenchSaga, BenchState>();
        }, instrument);

        try
        {
            var tracker = host.Services.GetRequiredService<WorkTracker>();
            await StartAndWarmUpAsync(host, persistence, tracker);

            tracker.Reset();
            warmedUp(host.Services);

            var start = Stopwatch.GetTimestamp();
            await BenchHost.RunConcurrentlyAsync(sagas, PublishParallelism, _ => PublishStartAsync(host.Services));
            await BenchHost.WaitUntilAsync(() => tracker.Count >= sagas, cap - Stopwatch.GetElapsedTime(start));

            return completed(host.Services, tracker, start);
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

    private static MessageEnvelope NewStart(ISystemInfo systemInfo)
        => MessageEnvelope.Create(new StartBench { CorrelationId = Guid.NewGuid().ToString() }, systemInfo);

    private static async Task SeedOutboxAsync(IServiceProvider services, int messages)
    {
        const int chunkSize = 50;
        var systemInfo = services.GetRequiredService<ISystemInfo>();

        for (var seeded = 0; seeded < messages; seeded += chunkSize)
        {
            using var scope = services.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
            var chunk = Enumerable.Range(0, Math.Min(chunkSize, messages - seeded))
                .Select(_ => NewStart(systemInfo))
                .ToArray();
            await repository.AppendAsync(chunk);
        }
    }
}

/// <param name="Sagas">sagas that completed.</param>
/// <param name="ElapsedMilliseconds">from the first publish to the last completion.</param>
/// <param name="Stages">calls and total time per wrapped call.</param>
internal sealed record EndToEndProfile(
    int Sagas,
    double ElapsedMilliseconds,
    IReadOnlyList<(string Stage, int Calls, double TotalMilliseconds)> Stages);
