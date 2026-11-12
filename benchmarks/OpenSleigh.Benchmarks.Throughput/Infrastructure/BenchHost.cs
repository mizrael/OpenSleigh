using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenSleigh.Benchmarks.Throughput.Workload;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Persistence;
using System.Diagnostics;

namespace OpenSleigh.Benchmarks.Throughput.Infrastructure;

internal static class BenchHost
{
    private const string ClientGroupKey = "OpenSleigh:ClientGroup";

    /// <summary>
    /// Builds a host the way an application would, through AddOpenSleigh() and the provider extension methods,
    /// leaving every library option at its default.
    /// </summary>
    public static IHost Build(
        ScenarioContext context,
        Action<IBusConfigurator> configureBus,
        Action<IServiceCollection>? overrideServices = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [ClientGroupKey] = $"bench-{context.RunId}" })
            .Build();

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.AddProvider(context.Problems);

        builder.Services.AddSingleton<WorkTracker>();
        builder.Services.AddOpenSleigh(configureBus, configuration);
        overrideServices?.Invoke(builder.Services);

        // not every persistence/transport combination registers a transaction manager
        builder.Services.TryAddSingleton<ITransactionManager, NoOpTransactionManager>();

        return builder.Build();
    }

    public static async Task StopAndDisposeAsync(IHost host, Func<IServiceProvider, Task>? cleanUpAsync = null)
    {
        try
        {
            await host.StopAsync(TimeSpan.FromSeconds(15));
        }
        catch
        {
            // shutdown problems must not turn a measured run into a failure
        }

        try
        {
            if (cleanUpAsync is not null)
                await cleanUpAsync(host.Services);
        }
        catch
        {
            // best effort: the service containers are thrown away with the job
        }

        try
        {
            if (host is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
            else
                host.Dispose();
        }
        catch
        {
        }
    }

    public static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > timeout)
                return false;
            await Task.Delay(5);
        }
        return true;
    }

    public static async Task RunConcurrentlyAsync(int count, int degreeOfParallelism, Func<int, Task> action)
    {
        var options = new ParallelOptions { MaxDegreeOfParallelism = degreeOfParallelism };
        await Parallel.ForEachAsync(Enumerable.Range(0, count), options, async (i, _) => await action(i));
    }
}

internal sealed class NoOpTransactionManager : ITransactionManager
{
    public ValueTask<ITransaction> StartTransactionAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult<ITransaction>(NoOpTransaction.Instance);
}
