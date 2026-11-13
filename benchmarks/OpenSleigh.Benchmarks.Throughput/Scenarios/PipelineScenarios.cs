using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OpenSleigh.Persistence;
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
    private static readonly TimeSpan RelaySettleTime = TimeSpan.FromSeconds(2);
    private const int RelayWarmUpMessages = 30;
    private const int PublishParallelism = 8;
    private const int SeedChunkSize = 50;

#if DELAYED_DELIVERY
    private const int DelayedBacklogChunkSize = 500;
    private static readonly TimeSpan DelayedBacklogDelay = TimeSpan.FromDays(1);
#endif

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
            // every caller opens its connection and compiles what it runs before the measurement starts
            await BenchHost.RunConcurrentlyAsync(parallelism * 4, parallelism, _ => PublishStartAsync(host.Services));

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
    /// <param name="delayedBacklog">
    /// messages that are not due during the run, waiting in the same outbox. They are neither relayed nor counted.
    /// </param>
    public static Task<double> OutboxRelayAsync(
        ScenarioContext context,
        PersistenceTarget persistence,
        int messages,
        TimeSpan cap,
        TransportTarget? transport = null,
        int delayedBacklog = 0)
        => RunOutboxRelayAsync(context, persistence, messages, cap, transport, delayedBacklog,
            instrument: null,
            completed: (_, relayed, elapsed) => elapsed > TimeSpan.Zero ? relayed / elapsed.TotalSeconds : 0);

    /// <summary>
    /// The same run as <see cref="OutboxRelayAsync"/>, reporting how long the relay spent in each call.
    /// </summary>
    public static Task<EndToEndProfile> ProfileOutboxRelayAsync(
        ScenarioContext context,
        PersistenceTarget persistence,
        int messages,
        TimeSpan cap,
        TransportTarget? transport = null)
        => RunOutboxRelayAsync(context, persistence, messages, cap, transport, delayedBacklog: 0,
            instrument: StageTimings.Instrument,
            completed: (services, relayed, elapsed) => new EndToEndProfile(
                relayed,
                elapsed.TotalMilliseconds,
                services.GetRequiredService<StageTimings>().Snapshot()));

    private static async Task<T> RunOutboxRelayAsync<T>(
        ScenarioContext context,
        PersistenceTarget persistence,
        int messages,
        TimeSpan cap,
        TransportTarget? transport,
        int delayedBacklog,
        Action<IServiceCollection>? instrument,
        Func<IServiceProvider, int, TimeSpan, T> completed)
    {
        if (transport is not null)
            await transport.PrepareAsync();

        var commits = new CommitClock();

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

            services.TryAddSingleton<ITransactionManager, BenchTransactionManager>();
            services.TryDecorate<ITransactionManager>((_, inner) => new CommitTrackingTransactionManager(inner, commits));
            instrument?.Invoke(services);
        });

        try
        {
            var tracker = host.Services.GetRequiredService<WorkTracker>();

            // the first publish declares exchanges and queues
            var systemInfo = host.Services.GetRequiredService<ISystemInfo>();
            await host.Services.GetRequiredService<IPublisher>().PublishAsync(NewStart(systemInfo));
            tracker.Reset();

            // the waiting messages are there from the start, so the warm-up reads the same outbox as the measurement
            await SeedDelayedBacklogAsync(host.Services, delayedBacklog);

            // the first passes compile queries and code that every later pass reuses: they are not what is measured.
            // They run here, before the relay is started, so that it cannot take measured messages early
            await SeedOutboxAsync(host.Services, RelayWarmUpMessages);
            await RelayUntilAsync(host.Services, () => tracker.Count >= RelayWarmUpMessages);

            await SeedOutboxAsync(host.Services, messages);
            tracker.Reset();
            commits.Reset();
            host.Services.GetService<StageTimings>()?.Reset();

            var start = Stopwatch.GetTimestamp();
            await host.StartAsync();
            await BenchHost.WaitUntilAsync(() => tracker.Count >= messages, cap);

            // a message is relayed once its pass has removed it from the outbox and committed, not when it is published
            await BenchHost.WaitUntilAsync(() => commits.Last >= tracker.LastCompletion, RelaySettleTime);

            return completed(host.Services, tracker.Count, Stopwatch.GetElapsedTime(start, Math.Max(commits.Last, tracker.LastCompletion)));
        }
        finally
        {
            await BenchHost.StopAndDisposeAsync(host, persistence.DropAsync);
        }
    }

    /// <summary>
    /// Messages that are all in the broker before the subscriber starts, consumed into a one-message saga
    /// on the in-memory persistence: the consumer never waits for a publisher.
    /// </summary>
    public static async Task<double> ConsumeBacklogAsync(
        ScenarioContext context,
        TransportTarget transport,
        int messages,
        int publishParallelism,
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

            await BenchHost.RunConcurrentlyAsync(messages, publishParallelism,
                async _ => await publisher.PublishAsync(NewPing(systemInfo)));

            await host.StartAsync();
            await BenchHost.WaitUntilAsync(() => tracker.Count >= messages, cap);

            return tracker.RateBetweenFirstAndLast();
        }
        finally
        {
            await BenchHost.StopAndDisposeAsync(host);
        }
    }

    /// <summary>
    /// Messages handed to the transport's publisher by concurrent callers, each waiting for its acknowledgement.
    /// Nothing consumes them.
    /// </summary>
    public static async Task<double> PublishConcurrentlyAsync(ScenarioContext context, TransportTarget transport, int messages, int parallelism)
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
            await BenchHost.RunConcurrentlyAsync(messages, parallelism,
                async _ => await publisher.PublishAsync(NewPing(systemInfo)));
            stopwatch.Stop();

            return messages / stopwatch.Elapsed.TotalSeconds;
        }
        finally
        {
            await BenchHost.StopAndDisposeAsync(host);
        }
    }

    /// <summary>
    /// Messages handed to the transport's publisher one after the other, the way the relay does. Nothing consumes them.
    /// </summary>
    public static Task<double> PublishAsync(ScenarioContext context, TransportTarget transport, int messages)
        => PublishConcurrentlyAsync(context, transport, messages, parallelism: 1);

    private static async Task RelayUntilAsync(IServiceProvider services, Func<bool> done)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!done())
        {
            if (stopwatch.Elapsed > WarmUpTimeout)
                throw new TimeoutException("warm-up messages were not relayed");

            using var scope = services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IOutboxProcessor>().RelayPendingMessagesAsync();
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

#if DELAYED_DELIVERY
    private static MessageEnvelope NewDelayedStart(ISystemInfo systemInfo)
        => MessageEnvelope.Create(new StartBench { CorrelationId = Guid.NewGuid().ToString() }, systemInfo, DelayedBacklogDelay);
#endif

    private static Task SeedDelayedBacklogAsync(IServiceProvider services, int messages)
    {
        if (messages == 0)
            return Task.CompletedTask;

#if DELAYED_DELIVERY
        return SeedOutboxAsync(services, messages, DelayedBacklogChunkSize, NewDelayedStart);
#else
        throw new NotSupportedException("the checkout the harness is built against has no delayed delivery");
#endif
    }

    private static Task SeedOutboxAsync(IServiceProvider services, int messages)
        => SeedOutboxAsync(services, messages, SeedChunkSize, NewStart);

    private static async Task SeedOutboxAsync(
        IServiceProvider services,
        int messages,
        int chunkSize,
        Func<ISystemInfo, MessageEnvelope> newEnvelope)
    {
        var systemInfo = services.GetRequiredService<ISystemInfo>();

        for (var seeded = 0; seeded < messages; seeded += chunkSize)
        {
            using var scope = services.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
            var chunk = Enumerable.Range(0, Math.Min(chunkSize, messages - seeded))
                .Select(_ => newEnvelope(systemInfo))
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
