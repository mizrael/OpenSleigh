using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace OpenSleigh.E2ETests.Harness.FaultInjection;

/// <summary>
/// every connection the hosts of a scenario opened to RabbitMQ, under a name no other scenario uses: to find them
/// at the broker, and to tell how many the hosts asked for and how many of them they still have.
/// A connection that the client library recovers by itself stays the same connection.
/// A host closes its connection when it stops: the log closes nothing.
/// </summary>
public sealed class RabbitMqConnectionLog
{
    private readonly ConcurrentQueue<IConnection> _opened = new();
    private int _closures;

    /// <summary>the name the connections of the hosts have at the broker.</summary>
    public string Name { get; } = $"e2e-{Guid.NewGuid():N}";

    /// <summary>how many connections the hosts asked for, open or not.</summary>
    public int Opened => _opened.Count;

    /// <summary>how many of the connections the hosts asked for are open now.</summary>
    public int Open => _opened.Count(connection => connection.IsOpen);

    /// <summary>how many times a connection of the hosts was closed, by whoever closed it.</summary>
    public int Closures => Volatile.Read(ref _closures);

    internal async Task<IConnection> OpenAsync(Func<Task<IConnection>> open)
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
