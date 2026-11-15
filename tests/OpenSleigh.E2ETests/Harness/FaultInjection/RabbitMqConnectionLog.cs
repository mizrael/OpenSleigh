using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RabbitMQ.Client;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Harness.FaultInjection;

/// <summary>
/// every connection the hosts of a scenario opened to RabbitMQ, under a name no other scenario uses: to find them
/// at the broker, to tell how many of them the hosts still have, and to close them when the scenario is over,
/// however it ended. A connection that the client library recovers by itself stays the same connection.
/// </summary>
/// <remarks>
/// A host closes the connection it uses when it stops, not one it has replaced: those would stay open, and keep
/// recovering, on a broker that every test shares. Disposing the log closes them.
/// </remarks>
public sealed class RabbitMqConnectionLog(ITestOutputHelper console) : IAsyncDisposable
{
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(20);

    private readonly ConcurrentQueue<IConnection> _opened = new();
    private readonly ConcurrentQueue<IConnection> _closed = new();
    private int _opening;
    private int _isClosing;
    private int _closures;

    /// <summary>the name the connections of the hosts have at the broker.</summary>
    public string Name { get; } = $"e2e-{Guid.NewGuid():N}";

    /// <summary>how many of the connections the hosts asked for are open now.</summary>
    public int Open => _opened.Count(connection => connection.IsOpen);

    /// <summary>how many times a connection of the hosts was closed, by whoever closed it.</summary>
    public int Closures => Volatile.Read(ref _closures);

    internal async Task<IConnection> OpenAsync(Func<Task<IConnection>> open)
    {
        // a host opens a connection whenever one it had is closed, also when it is the host that closed it,
        // because it stopped (see the known defect about connections kept after a reconnect): closing the ones
        // it left behind would make it open as many again, for ever. It is given one that was never opened
        if (Volatile.Read(ref _isClosing) != 0)
            return Substitute.For<IConnection>();

        Interlocked.Increment(ref _opening);
        try
        {
            var connection = await open();
            connection.ConnectionShutdownAsync += (_, _) =>
            {
                Interlocked.Increment(ref _closures);
                return Task.CompletedTask;
            };
            _opened.Enqueue(connection);
            return connection;
        }
        finally
        {
            Interlocked.Decrement(ref _opening);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Volatile.Write(ref _isClosing, 1);

        // a connection that was being opened when the closing began shows up afterwards
        do
        {
            while (_opened.TryDequeue(out var connection))
            {
                await CloseAsync(connection);
                _closed.Enqueue(connection);
            }
        } while (await IsOpeningAsync() || !_opened.IsEmpty);

        var leftOpen = _closed.Count(connection => connection.IsOpen);
        if (leftOpen > 0)
            ReportCleanUpFailure($"{leftOpen} of them are still open.");
    }

    // what goes wrong here is written to the output, as the run does for its own clean-up:
    // it never replaces what the scenario failed with
    private async Task CloseAsync(IConnection connection)
    {
        try
        {
            await connection.DisposeAsync().AsTask().WaitAsync(CloseTimeout);
        }
        catch (Exception e)
        {
            ReportCleanUpFailure(e.ToString());
        }
    }

    private async Task<bool> IsOpeningAsync()
    {
        using var timeout = new CancellationTokenSource(CloseTimeout);
        while (Volatile.Read(ref _opening) > 0 && !timeout.IsCancellationRequested)
            await Task.Delay(PollingInterval);

        return Volatile.Read(ref _opening) > 0;
    }

    private void ReportCleanUpFailure(string problem)
        => console.WriteLine($"{ScenarioRun.CleanUpFailed}: the RabbitMQ connections '{Name}' may have been left behind. {problem}");
}

public static class RabbitMqConnectionLogExtensions
{
    /// <summary>
    /// names every connection a host opens to RabbitMQ after the log, and records it there.
    /// Call it from <see cref="HostSetup.ConfigureServices"/>, on a combination that uses RabbitMQ.
    /// </summary>
    public static IServiceCollection TrackRabbitMqConnections(this IServiceCollection services, RabbitMqConnectionLog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        return services.Decorate<IConnectionFactory>((_, factory) =>
        {
            factory.ClientProvidedName = log.Name;
            return new LoggingConnectionFactory(factory, log);
        });
    }

    private sealed class LoggingConnectionFactory(IConnectionFactory inner, RabbitMqConnectionLog log) : IConnectionFactory
    {
        public IDictionary<string, object?> ClientProperties
        {
            get => inner.ClientProperties;
            set => inner.ClientProperties = value;
        }

        public string Password
        {
            get => inner.Password;
            set => inner.Password = value;
        }

        public ushort RequestedChannelMax
        {
            get => inner.RequestedChannelMax;
            set => inner.RequestedChannelMax = value;
        }

        public uint RequestedFrameMax
        {
            get => inner.RequestedFrameMax;
            set => inner.RequestedFrameMax = value;
        }

        public TimeSpan RequestedHeartbeat
        {
            get => inner.RequestedHeartbeat;
            set => inner.RequestedHeartbeat = value;
        }

        public string UserName
        {
            get => inner.UserName;
            set => inner.UserName = value;
        }

        public string VirtualHost
        {
            get => inner.VirtualHost;
            set => inner.VirtualHost = value;
        }

        public ICredentialsProvider? CredentialsProvider
        {
            get => inner.CredentialsProvider;
            set => inner.CredentialsProvider = value;
        }

        public Uri Uri
        {
            get => inner.Uri;
            set => inner.Uri = value;
        }

        public string? ClientProvidedName
        {
            get => inner.ClientProvidedName;
            set => inner.ClientProvidedName = value;
        }

        public TimeSpan HandshakeContinuationTimeout
        {
            get => inner.HandshakeContinuationTimeout;
            set => inner.HandshakeContinuationTimeout = value;
        }

        public TimeSpan ContinuationTimeout
        {
            get => inner.ContinuationTimeout;
            set => inner.ContinuationTimeout = value;
        }

        public ushort ConsumerDispatchConcurrency
        {
            get => inner.ConsumerDispatchConcurrency;
            set => inner.ConsumerDispatchConcurrency = value;
        }

        public IAuthMechanismFactory? AuthMechanismFactory(IEnumerable<string> mechanismNames)
            => inner.AuthMechanismFactory(mechanismNames);

        public Task<IConnection> CreateConnectionAsync(CancellationToken cancellationToken = default)
            => log.OpenAsync(() => inner.CreateConnectionAsync(cancellationToken));

        public Task<IConnection> CreateConnectionAsync(string clientProvidedName, CancellationToken cancellationToken = default)
            => log.OpenAsync(() => inner.CreateConnectionAsync(clientProvidedName, cancellationToken));

        public Task<IConnection> CreateConnectionAsync(IEnumerable<string> hostnames, CancellationToken cancellationToken = default)
            => log.OpenAsync(() => inner.CreateConnectionAsync(hostnames, cancellationToken));

        public Task<IConnection> CreateConnectionAsync(IEnumerable<string> hostnames, string clientProvidedName, CancellationToken cancellationToken = default)
            => log.OpenAsync(() => inner.CreateConnectionAsync(hostnames, clientProvidedName, cancellationToken));

        public Task<IConnection> CreateConnectionAsync(IEnumerable<AmqpTcpEndpoint> endpoints, CancellationToken cancellationToken = default)
            => log.OpenAsync(() => inner.CreateConnectionAsync(endpoints, cancellationToken));

        public Task<IConnection> CreateConnectionAsync(IEnumerable<AmqpTcpEndpoint> endpoints, string clientProvidedName, CancellationToken cancellationToken = default)
            => log.OpenAsync(() => inner.CreateConnectionAsync(endpoints, clientProvidedName, cancellationToken));
    }
}
