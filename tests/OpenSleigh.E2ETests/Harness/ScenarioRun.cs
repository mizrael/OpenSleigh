using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenSleigh.DependencyInjection;
using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.Outbox;
using OpenSleigh.Queries;
using OpenSleigh.Transport;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Harness;

/// <summary>
/// one scenario on one combination: its store, its transport and its hosts.
/// Disposing it stops the hosts that still run and removes what the store and the transport created.
/// </summary>
public sealed class ScenarioRun : IAsyncDisposable
{
    // an upper bound, spent only by hosts that fail to start
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan OutboxInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(50);

    private const string ClientGroupSetting = "OpenSleigh:ClientGroup";

    private readonly ITestOutputHelper _console;
    private readonly IStore _store;
    private readonly ITransport _transport;
    private readonly List<ScenarioHost> _hosts = [];
    private readonly object _hostsGate = new();
    private readonly ConcurrentQueue<ProcessingFailure> _processingFailures = new();

    private ScenarioRun(Combination combination, RunContext context, ITestOutputHelper console)
    {
        Combination = combination;
        RunId = context.RunId;
        _console = console;
        _store = combination.CreateStore(context);
        _transport = combination.CreateTransport(context);
    }

    internal static ScenarioRun Create(Combination combination, ITestOutputHelper console)
    {
        ArgumentNullException.ThrowIfNull(combination);
        ArgumentNullException.ThrowIfNull(console);

        return new ScenarioRun(combination, RunContext.Create(), console);
    }

    public Combination Combination { get; }

    public string RunId { get; }

    /// <summary>every host started so far, in the order they were started, stopped ones included.</summary>
    public IReadOnlyList<ScenarioHost> Hosts
    {
        get
        {
            lock (_hostsGate)
                return _hosts.ToArray();
        }
    }

    /// <summary>the first host that is running: the one a scenario publishes from when it does not care which.</summary>
    /// <exception cref="InvalidOperationException">no host is running.</exception>
    public ScenarioHost AnyHost
        => Hosts.FirstOrDefault(host => host.IsRunning)
           ?? throw new InvalidOperationException("no host of this scenario is running.");

    public bool SupportsDeadLetters => _transport.SupportsDeadLetters;

    /// <summary>
    /// builds a number of identical hosts and starts them together, the way replicas of a service start.
    /// </summary>
    /// <exception cref="TimeoutException">the hosts did not start in time.</exception>
    public async Task<IReadOnlyList<ScenarioHost>> StartHostsAsync(int count, HostSetup setup)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentNullException.ThrowIfNull(setup);

        if (count > 1 && !Combination.HostsShareInfrastructure)
            throw new InvalidOperationException(
                $"hosts on '{Combination.Name}' share nothing: a scenario cannot use more than one.");

        var hosts = Enumerable.Range(0, count).Select(_ => new ScenarioHost(BuildHost(setup))).ToArray();
        lock (_hostsGate)
            _hosts.AddRange(hosts);

        using var timeout = new CancellationTokenSource(StartupTimeout);
        try
        {
            foreach (var host in hosts)
            {
                await _store.PrepareAsync(host.Services, timeout.Token);
                await _transport.PrepareAsync(host.Services, timeout.Token);
            }

            await Task.WhenAll(hosts.Select(host => host.StartAsync(timeout.Token)));
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException($"the hosts did not start within {StartupTimeout.TotalSeconds} seconds.");
        }

        return hosts;
    }

    /// <summary>
    /// starts one more host: another replica, a replacement for one that was stopped, or a host of another service.
    /// </summary>
    public async Task<ScenarioHost> StartHostAsync(HostSetup setup)
        => (await StartHostsAsync(1, setup))[0];

    /// <summary>
    /// reads a saga from the store, over and over, until it is stored the way the scenario expects.
    /// </summary>
    public async Task<SagaInstanceInfo> WaitForSagaAsync<TSaga>(
        string correlationId,
        Func<SagaInstanceInfo, bool> isAsExpected,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(isAsExpected);

        while (true)
        {
            var saga = await FindSagaAsync<TSaga>(correlationId, cancellationToken);
            if (saga is not null && isAsExpected(saga))
                return saga;

            await Task.Delay(PollingInterval, cancellationToken);
        }
    }

    /// <summary>reads a saga from the store. Null when it is not there.</summary>
    public async Task<SagaInstanceInfo?> FindSagaAsync<TSaga>(string correlationId, CancellationToken cancellationToken)
    {
        await using var scope = AnyHost.Services.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<ISagaStateQuery>();
        return await query.GetByCorrelationIdAsync(correlationId, typeof(TSaga).FullName!, cancellationToken);
    }

    /// <summary>how many messages are in the outbox, relayed or not.</summary>
    public Task<long> CountOutboxMessagesAsync(CancellationToken cancellationToken)
        => _store.CountOutboxMessagesAsync(AnyHost.Services, cancellationToken);

    /// <summary>
    /// how many messages of each type were dead-lettered, over the message types the hosts of this run handle.
    /// Types with no dead letter are left out.
    /// </summary>
    /// <exception cref="NotSupportedException">the transport has no dead letters: see <see cref="SupportsDeadLetters"/>.</exception>
    public Task<IReadOnlyDictionary<Type, long>> ReadDeadLettersAsync(CancellationToken cancellationToken)
        => _transport.ReadDeadLettersAsync(cancellationToken);

    /// <summary>
    /// every message a host of this run gave back to its transport with an exception, so far:
    /// the reason behind a retry or a dead letter.
    /// </summary>
    public IReadOnlyCollection<ProcessingFailure> ProcessingFailures => _processingFailures;

    /// <summary>
    /// stops every host that is still running.
    /// </summary>
    /// <exception cref="AggregateException">a host did not stop cleanly. The others are stopped all the same.</exception>
    public async Task StopHostsAsync()
    {
        var failures = new List<Exception>();
        foreach (var host in Hosts)
        {
            try
            {
                await host.StopAsync();
            }
            catch (AggregateException e)
            {
                failures.AddRange(e.InnerExceptions);
            }
        }

        if (failures.Count > 0)
            throw new AggregateException("the hosts did not stop cleanly.", failures);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopHostsAsync();
        }
        catch (AggregateException e)
        {
            // a scenario that cares stops its hosts itself: here they are only cleaned up
            _console.WriteLine($"the hosts did not stop cleanly: {e}");
        }

        await _transport.DisposeAsync();
        await _store.DisposeAsync();
    }

    private IHost BuildHost(HostSetup setup)
    {
        // every host of a service is in the same client group, and no other run is
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [ClientGroupSetting] = $"{RunId}.{setup.Service}" })
            .Build();

        return new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(new HostIdentity(setup.Service));

                services.AddLogging(logging => logging
                    .ClearProviders()
                    .SetMinimumLevel(LogLevel.Trace)
                    .AddFilter("Microsoft", LogLevel.Warning)
                    .AddProvider(new TestOutputLoggerProvider(_console)));

                services.AddOpenSleigh(bus =>
                {
                    _store.Configure(bus);
                    _transport.Configure(bus, setup.Service);
                    bus.WithOutboxProcessorOptions(new OutboxProcessorOptions(OutboxInterval));

                    setup.ConfigureBus(bus);
                }, configuration);

                setup.ConfigureServices?.Invoke(services);

                // last, to see what the decorators of the scenario throw as well
                services.Decorate<IMessageProcessor>((_, inner)
                    => new RecordingMessageProcessor(inner, setup.Service, _processingFailures));
            })
            .Build();
    }
}
