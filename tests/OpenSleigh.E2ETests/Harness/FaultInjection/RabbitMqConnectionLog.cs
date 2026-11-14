using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace OpenSleigh.E2ETests.Harness.FaultInjection;

/// <summary>
/// every connection the hosts of a scenario opened to RabbitMQ, to tell how many of them they still have.
/// A connection that the client library recovers by itself stays the same connection.
/// </summary>
public sealed class RabbitMqConnectionLog
{
    private readonly ConcurrentQueue<IConnection> _opened = new();

    /// <summary>how many of the connections the hosts asked for are open now.</summary>
    public int Open => _opened.Count(connection => connection.IsOpen);

    internal void Add(IConnection connection) => _opened.Enqueue(connection);

    /// <summary>
    /// closes every connection the hosts opened, once they are stopped: a host closes the connection it uses,
    /// not one it has replaced, which would otherwise stay open until the tests end.
    /// </summary>
    public async Task CloseAllAsync()
    {
        foreach (var connection in _opened)
            await connection.DisposeAsync();
    }
}

public static class RabbitMqConnectionLogExtensions
{
    /// <summary>
    /// records in the log every connection a host opens to RabbitMQ.
    /// Call it from <see cref="HostSetup.ConfigureServices"/>, on a combination that uses RabbitMQ.
    /// </summary>
    public static IServiceCollection LogRabbitMqConnections(this IServiceCollection services, RabbitMqConnectionLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        return services.Decorate<IConnectionFactory>((_, factory) => new LoggingConnectionFactory(factory, log));
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
            => LogAsync(inner.CreateConnectionAsync(cancellationToken));

        public Task<IConnection> CreateConnectionAsync(string clientProvidedName, CancellationToken cancellationToken = default)
            => LogAsync(inner.CreateConnectionAsync(clientProvidedName, cancellationToken));

        public Task<IConnection> CreateConnectionAsync(IEnumerable<string> hostnames, CancellationToken cancellationToken = default)
            => LogAsync(inner.CreateConnectionAsync(hostnames, cancellationToken));

        public Task<IConnection> CreateConnectionAsync(IEnumerable<string> hostnames, string clientProvidedName, CancellationToken cancellationToken = default)
            => LogAsync(inner.CreateConnectionAsync(hostnames, clientProvidedName, cancellationToken));

        public Task<IConnection> CreateConnectionAsync(IEnumerable<AmqpTcpEndpoint> endpoints, CancellationToken cancellationToken = default)
            => LogAsync(inner.CreateConnectionAsync(endpoints, cancellationToken));

        public Task<IConnection> CreateConnectionAsync(IEnumerable<AmqpTcpEndpoint> endpoints, string clientProvidedName, CancellationToken cancellationToken = default)
            => LogAsync(inner.CreateConnectionAsync(endpoints, clientProvidedName, cancellationToken));

        private async Task<IConnection> LogAsync(Task<IConnection> opening)
        {
            var connection = await opening;
            log.Add(connection);
            return connection;
        }
    }
}
