
using Microsoft.Extensions.Logging;
using Polly;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OpenSleigh.Transport.RabbitMQ;

/// <summary>
/// the one connection of a host to RabbitMQ. It is opened once, when the first channel is asked for, and closed when
/// this is disposed. A connection that is lost is recovered by the client library (the transport turns its automatic
/// recovery on), together with its channels and its consumers: this never opens another one in its place.
/// </summary>
public sealed class RabbitPersistentConnection : IDisposable, IBusConnection
{
    private readonly ILogger<RabbitPersistentConnection> _logger;
    private readonly IConnectionFactory _connectionFactory;
    private readonly CancellationTokenSource _disposing = new();
    private volatile IConnection? _connection;
    private volatile bool _disposed;

    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public RabbitPersistentConnection(IConnectionFactory connectionFactory, ILogger<RabbitPersistentConnection> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsConnected => !_disposed && _connection is { IsOpen: true };

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // not disposed: a connection may be being opened with its token, and it holds nothing to release
        _disposing.Cancel();
        Close(_connection);
    }

    private void Close(IConnection? connection)
    {
        if (connection is null)
            return;

        connection.ConnectionShutdownAsync -= OnConnectionShutdownAsync;
        connection.Dispose();
    }

    private Task OnConnectionShutdownAsync(object sender, ShutdownEventArgs shutdown)
    {
        if (!_disposed && shutdown.Initiator != ShutdownInitiator.Application)
            _logger.LogWarning(
                "the RabbitMQ connection was closed: {Reason}. The client library recovers it, with its channels and consumers; " +
                "no channel can be created until then.",
                shutdown.ReplyText);

        return Task.CompletedTask;
    }

    private async ValueTask<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_connection is { } connection)
            return connection;

        using var connecting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposing.Token);
        await _semaphore.WaitAsync(connecting.Token);

        try
        {
            return _connection ?? await ConnectAsync(connecting.Token);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async ValueTask<IConnection> ConnectAsync(CancellationToken cancellationToken)
    {
        var policy = Policy
                    .Handle<Exception>()
                    .WaitAndRetryAsync(5,
                        retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                        (ex, timeSpan, context) =>
                        {
                            _logger.LogError(ex, $"an exception has occurred while opening RabbitMQ connection: {ex.Message}");
                        });

        var connection = await policy.ExecuteAsync(_connectionFactory.CreateConnectionAsync, cancellationToken);
        connection.ConnectionShutdownAsync += OnConnectionShutdownAsync;
        _connection = connection;

        // Dispose reads the connection after it sets the flag, and this reads the flag after it sets the connection:
        // one of the two closes a connection that was being opened while the owner was disposed
        if (_disposed)
        {
            Close(connection);
            throw new ObjectDisposedException(nameof(RabbitPersistentConnection));
        }

        return connection;
    }

    /// <exception cref="InvalidOperationException">
    /// the connection is lost and has not been recovered yet, or this was disposed (<see cref="ObjectDisposedException"/>).
    /// </exception>
    public async Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        if (!connection.IsOpen)
            throw new InvalidOperationException("No RabbitMQ connections are available to perform this action: the connection is closed, and is being recovered");

        return await connection.CreateChannelAsync(cancellationToken: cancellationToken);
    }
}
