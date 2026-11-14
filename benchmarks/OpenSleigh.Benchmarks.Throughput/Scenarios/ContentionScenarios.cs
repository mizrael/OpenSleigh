#if RABBIT_CONSUMER_TUNING
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenSleigh.Benchmarks.Throughput.Infrastructure;
using OpenSleigh.Benchmarks.Throughput.Workload;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Transport;
using System.Diagnostics;

namespace OpenSleigh.Benchmarks.Throughput.Scenarios;

/// <summary>
/// SPIKE: several hosts, each with several consumers, working on sagas whose messages arrive together.
/// Every scenario reports, besides its rate, how often a handler ran for nothing.
/// </summary>
internal static class ContentionScenarios
{
    private static readonly TimeSpan WarmUpTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan SubscriptionSettleTime = TimeSpan.FromSeconds(1);
    private const int PublishParallelism = 8;
    private const int WarmUpUnits = 12;
    private const ushort PrefetchPerConsumer = 4;

    /// <summary>
    /// Orders per second. One order is an orchestrator saga that sends four commands to four worker sagas
    /// and completes when their four replies, which arrive at about the same time, are all handled.
    /// </summary>
    public static Task<double> OrderWorkflowAsync(
        ScenarioContext context, string name, PersistenceTarget persistence,
        int hosts, ushort consumers, int orders, TimeSpan handlerDelay, TimeSpan cap)
        => RunAsync(context, name, persistence, hosts, consumers, orders, handlerDelay, cap,
            cfg => cfg
                .AddSaga<OrderSaga, OrderState>()
                .AddSaga<StockSaga>()
                .AddSaga<PaymentSaga>()
                .AddSaga<ShippingSaga>()
                .AddSaga<NotificationSaga>(),
            correlationId => new StartOrder { CorrelationId = correlationId },
            handlerRunsPerUnit: OrderSaga.HandlerRunsPerOrder,
            messagesPerUnit: 1);

    /// <summary>
    /// Messages per second. One saga publishes <paramref name="items"/> different messages to itself,
    /// which reach the consumers together, and completes when all of them are handled.
    /// </summary>
    public static Task<double> BurstAsync(
        ScenarioContext context, string name, PersistenceTarget persistence,
        int hosts, ushort consumers, int sagas, int items, TimeSpan handlerDelay, TimeSpan cap)
        => RunAsync(context, name, persistence, hosts, consumers, sagas, handlerDelay, cap,
            cfg => cfg.AddSaga<BurstSaga, BurstState>(),
            correlationId => new StartBurst { CorrelationId = correlationId, Items = items },
            handlerRunsPerUnit: items + 1,
            messagesPerUnit: items);

    private static async Task<double> RunAsync<TStart>(
        ScenarioContext context, string name, PersistenceTarget persistence,
        int hostsCount, ushort consumers, int units, TimeSpan handlerDelay, TimeSpan cap,
        Action<IBusConfigurator> addSagas,
        Func<string, TStart> newStart,
        int handlerRunsPerUnit,
        int messagesPerUnit)
        where TStart : IMessage
    {
        var transport = TransportTarget.RabbitConcurrent(context, consumers, (ushort)(consumers * PrefetchPerConsumer));
        await transport.PrepareAsync();

        var probe = new ContentionProbe();
        var deliveries = new DeliveryCounter();

        var hosts = Enumerable.Range(0, hostsCount)
            .Select(_ => BenchHost.Build(context, cfg =>
            {
                persistence.Configure(cfg);
                transport.Configure(cfg);
                addSagas(cfg);
            }, services =>
            {
                services.AddSingleton(probe);
                services.AddSingleton(deliveries);
                services.TryDecorate<IMessageProcessor>((provider, inner)
                    => new CountingMessageProcessor(inner, provider.GetRequiredService<DeliveryCounter>()));
            }))
            .ToArray();

        try
        {
            persistence.Prepare(hosts[0].Services);
            foreach (var host in hosts)
                await host.StartAsync();

            // hosts subscribe in the background: the warm-up runs once they had the time to
            await Task.Delay(SubscriptionSettleTime);
            await PublishAsync(hosts, probe, WarmUpUnits, newStart);
            if (!await BenchHost.WaitUntilAsync(() => probe.Completed >= WarmUpUnits, WarmUpTimeout))
                throw new TimeoutException("warm-up did not complete");

            // late deliveries of the warm-up must not be counted
            await Task.Delay(SubscriptionSettleTime);
            probe.Reset();
            deliveries.Reset();
            SpikeMode.ResetCounters();
            probe.HandlerDelay = handlerDelay;

            var start = Stopwatch.GetTimestamp();
            await PublishAsync(hosts, probe, units, newStart);
            await BenchHost.WaitUntilAsync(() => probe.Completed >= units, cap - Stopwatch.GetElapsedTime(start));

            var completed = probe.Completed;
            var elapsed = Stopwatch.GetElapsedTime(start, Math.Max(probe.LastCompletion, start + 1)).TotalSeconds;

            // what is still on its way (a retry, a duplicate) is counted too
            await Task.Delay(SubscriptionSettleTime);

            var latencies = probe.LatenciesInMilliseconds.ToArray();
            var expectedRuns = (double)completed * handlerRunsPerUnit;
            var metrics = new List<(string, double)>
            {
                ("hosts", hostsCount),
                ("consumersPerHost", consumers),
                ("handlerDelayMs", handlerDelay.TotalMilliseconds),
                ("units", units),
                ("completed", completed),
                ("elapsedSeconds", elapsed),
                ("handlerRuns", probe.HandlerRuns),
                ("expectedHandlerRuns", expectedRuns),
                ("wastedHandlerRuns", probe.HandlerRuns - expectedRuns),
                ("rerunRate", expectedRuns > 0 ? (probe.HandlerRuns - expectedRuns) / expectedRuns : 0),
                ("overcount", probe.Overcount),
                ("latencyP50Ms", Percentile(latencies, 0.50)),
                ("latencyP95Ms", Percentile(latencies, 0.95)),
                ("latencyMaxMs", latencies.Length > 0 ? latencies.Max() : 0),
                ("deliveries", deliveries.Deliveries),
                ("lockConflictsToTransport", deliveries.LockConflicts),
                ("otherFailures", deliveries.OtherFailures),
            };
            metrics.AddRange(SpikeMode.Counters());
            SpikeMetrics.Record(name, metrics);

            if (completed < units)
                throw new TimeoutException($"only {completed} of {units} completed within {cap.TotalSeconds:N0} s");
            if (probe.Overcount != 0)
                throw new InvalidOperationException($"a saga applied {probe.Overcount} changes more than the messages it was sent");

            return completed * messagesPerUnit / elapsed;
        }
        finally
        {
            for (var i = hosts.Length - 1; i >= 0; i--)
                await BenchHost.StopAndDisposeAsync(hosts[i], i == 0 ? persistence.DropAsync : null);
        }
    }

    private static Task PublishAsync<TStart>(IHost[] hosts, ContentionProbe probe, int units, Func<string, TStart> newStart)
        where TStart : IMessage
        => BenchHost.RunConcurrentlyAsync(units, PublishParallelism, async i =>
        {
            var correlationId = Guid.NewGuid().ToString();
            probe.Start(correlationId);

            using var scope = hosts[i % hosts.Length].Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(newStart(correlationId));
        });

    private static double Percentile(double[] values, double percentile)
    {
        if (values.Length == 0)
            return 0;

        Array.Sort(values);
        return values[Math.Min(values.Length - 1, (int)Math.Ceiling(percentile * values.Length) - 1)];
    }
}
#endif
