using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Outbox;
using OpenSleigh.Queries;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests;

[Category("E2E")]
[Trait("Category", "E2E")]
public abstract class E2ETestsBase
{
    // upper bounds, spent only by a scenario that fails: one that works ends as soon as what it expects has happened
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ScenarioTimeout = TimeSpan.FromSeconds(60);

    // how long the hosts are left running once the scenario is over, for a message that should not
    // have been delivered, or delivered again, to show up. It spans several outbox passes and transport retries
    private static readonly TimeSpan ObservationWindow = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(50);

    protected readonly ITestOutputHelper Console;

    protected E2ETestsBase(ITestOutputHelper console)
    {
        Console = console ?? throw new ArgumentNullException(nameof(console));
    }

    /// <summary>
    /// starts the hosts, runs the scenario on the services of the first one, keeps the hosts running
    /// for the observation window and stops them.
    /// </summary>
    /// <param name="scenario">publishes the messages and waits for what it expects to happen.
    /// Its token is cancelled when the scenario takes too long.</param>
    /// <exception cref="TimeoutException">the scenario did not finish in time.</exception>
    protected async Task RunScenarioAsync(int hostsCount,
        Action<HostBuilderContext, IServiceCollection> configureServices,
        Func<IServiceProvider, CancellationToken, Task> scenario)
    {
        var hosts = Enumerable.Range(0, hostsCount)
            .Select(_ => CreateHostBuilder().ConfigureServices(configureServices).Build())
            .ToArray();

        try
        {
            await StartAsync(hosts);

            await using (var scope = hosts[0].Services.CreateAsyncScope())
                await RunWithTimeoutAsync(token => scenario(scope.ServiceProvider, token));

            await Task.Delay(ObservationWindow);
        }
        catch
        {
            // what failed above is what the test reports, whatever stopping the hosts has to say
            await StopAsync(hosts, throwOnFailure: false);
            throw;
        }

        await StopAsync(hosts, throwOnFailure: true);
    }

    /// <summary>
    /// reads a saga over and over until it is stored the way the scenario expects.
    /// </summary>
    protected static async Task<SagaInstanceInfo> WaitForSagaAsync<TSaga>(
        IServiceProvider services,
        string correlationId,
        Func<SagaInstanceInfo, bool> isAsExpected,
        CancellationToken cancellationToken)
    {
        var query = services.GetRequiredService<ISagaStateQuery>();
        while (true)
        {
            var saga = await query.GetByCorrelationIdAsync(correlationId, typeof(TSaga).FullName!, cancellationToken);
            if (saga is not null && isAsExpected(saga))
                return saga;

            await Task.Delay(PollingInterval, cancellationToken);
        }
    }

    private static async Task StartAsync(IReadOnlyCollection<IHost> hosts)
    {
        using var timeout = new CancellationTokenSource(StartupTimeout);
        try
        {
            await Task.WhenAll(hosts.Select(host => host.StartAsync(timeout.Token)));
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException($"the hosts did not start within {StartupTimeout.TotalSeconds} seconds.");
        }
    }

    private static async Task RunWithTimeoutAsync(Func<CancellationToken, Task> scenario)
    {
        using var timeout = new CancellationTokenSource(Debugger.IsAttached ? Timeout.InfiniteTimeSpan : ScenarioTimeout);
        try
        {
            await scenario(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"the scenario did not complete within {ScenarioTimeout.TotalSeconds} seconds: something it expected did not happen.");
        }
    }

    // every host is stopped and disposed, whatever the ones before it did: one left running keeps consuming
    // from queues and topics that the next tests use
    private static async Task StopAsync(IEnumerable<IHost> hosts, bool throwOnFailure)
    {
        var failures = new List<Exception>();
        foreach (var host in hosts)
        {
            await TryAsync(() => host.StopAsync(), failures);
            await TryAsync(() => DisposeAsync(host), failures);
        }

        if (throwOnFailure && failures.Count > 0)
            throw new AggregateException("the hosts did not stop cleanly.", failures);
    }

    private static async Task DisposeAsync(IHost host)
    {
        if (host is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        else
            host.Dispose();
    }

    private static async Task TryAsync(Func<Task> action, ICollection<Exception> failures)
    {
        try
        {
            await action();
        }
        catch (Exception e)
        {
            failures.Add(e);
        }
    }

    private IHostBuilder CreateHostBuilder() =>
        Host.CreateDefaultBuilder()
            .ConfigureServices((hostContext, services) =>
            {
                services.AddOpenSleigh(cfg =>
                {
                    ConfigureTransportAndPersistence(cfg);

                    var opts = new OutboxProcessorOptions(TimeSpan.FromMilliseconds(250));
                    cfg.WithOutboxProcessorOptions(opts);

                    RegisterSagas(cfg);
                });

                services.AddLogging(b => b.ClearProviders().AddProvider(new OutputLoggerProvider(Console)));
            });

    protected abstract void ConfigureTransportAndPersistence(IBusConfigurator cfg);
    protected abstract void RegisterSagas(IBusConfigurator cfg);

    private class OutputLoggerProvider(ITestOutputHelper output) : ILoggerProvider
    {
        public void Dispose() { }

        public ILogger CreateLogger(string categoryName) => new XUnitLogger(output, categoryName);
    }

    private class XUnitLogger(ITestOutputHelper testOutputHelper, string categoryName) : ILogger
    {
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => new Disposable();

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            try
            {
                testOutputHelper.WriteLine($"{categoryName}/{logLevel}: {formatter(state, exception)}");

                if (exception != null)
                    testOutputHelper.WriteLine(exception.ToString());
            }
            catch (InvalidOperationException)
            {
                // a host that is still shutting down logs after its test is over, when xunit takes no more output
            }
        }

        private class Disposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
