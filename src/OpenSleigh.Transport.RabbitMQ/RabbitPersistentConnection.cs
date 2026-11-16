
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
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private IConnection? _connection;
    private int _disposed;

    // from the loss of the connection until the client has recovered it with its channels and consumers. The client
    // reports the connection open before that, and a channel created in between is recovered a second time
    private volatile bool _lost;

    public RabbitPersistentConnection(IConnectionFactory connectionFactory, ILogger<RabbitPersistentConnection> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public bool IsConnected => !IsDisposed && !_lost && Volatile.Read(ref _connection) is { IsOpen: true };

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            Close(Interlocked.Exchange(ref _connection, null));
    }

    // called with what was taken out of the field: once for a connection, whoever closes it
    private void Close(IConnection? connection)
    {
        if (connection is null)
            return;

        connection.ConnectionShutdownAsync -= OnConnectionShutdownAsync;
        connection.ConnectionRecoveryErrorAsync -= OnRecoveryErrorAsync;
        connection.RecoverySucceededAsync -= OnRecoverySucceededAsync;
        connection.Dispose();
    }

    private Task OnConnectionShutdownAsync(object sender, ShutdownEventArgs shutdown)
    {
        if (IsDisposed)
            return Task.CompletedTask;

        _lost = true;

        if (IsRecoveredByTheClient(shutdown))
            _logger.LogWarning(
                "the RabbitMQ connection was closed: {Reason}. Nothing is published or consumed until the RabbitMQ client " +
                "has recovered it, which it does when its automatic recovery is enabled.",
                shutdown.ReplyText);
        else
            _logger.LogError(
                "the RabbitMQ connection was closed, and the RabbitMQ client does not recover it: {Reason}. " +
                "Nothing is published or consumed until the application is restarted.",
                shutdown.ReplyText);

        return Task.CompletedTask;
    }

    // as the client decides it, in its AutorecoveringConnection
    private static bool IsRecoveredByTheClient(ShutdownEventArgs shutdown)
        => shutdown.Initiator == ShutdownInitiator.Library
           || (shutdown.Initiator == ShutdownInitiator.Peer && shutdown.ReplyCode != Constants.AccessRefused);

    private Task OnRecoveryErrorAsync(object sender, ConnectionRecoveryErrorEventArgs error)
    {
        if (!IsDisposed)
            _logger.LogError(error.Exception,
                "the RabbitMQ connection could not be recovered: {Error}. The RabbitMQ client tries again.",
                error.Exception.Message);

        return Task.CompletedTask;
    }

    private Task OnRecoverySucceededAsync(object sender, AsyncEventArgs recovered)
    {
        _lost = false;
        _logger.LogInformation("the RabbitMQ connection was recovered, with its channels and consumers.");

        return Task.CompletedTask;
    }

    private async ValueTask<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (Volatile.Read(ref _connection) is { } connection)
            return connection;

        await _semaphore.WaitAsync(cancellationToken);

        try
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);

            return Volatile.Read(ref _connection) ?? await ConnectAsync(cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async ValueTask<IConnection> ConnectAsync(CancellationToken cancellationToken)
    {
        // neither a caller that gave up nor an owner that was disposed is a failure to try again
        var policy = Policy
                    .Handle<Exception>(_ => !cancellationToken.IsCancellationRequested && !IsDisposed)
                    .WaitAndRetryAsync(5,
                        retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                        (ex, timeSpan, context) =>
                        {
                            _logger.LogError(ex, $"an exception has occurred while opening RabbitMQ connection: {ex.Message}");
                        });

        IConnection connection;
        try
        {
            connection = await policy.ExecuteAsync(_connectionFactory.CreateConnectionAsync, cancellationToken);
        }
        catch (Exception) when (IsDisposed && !cancellationToken.IsCancellationRequested)
        {
            throw new ObjectDisposedException(nameof(RabbitPersistentConnection));
        }

        connection.ConnectionShutdownAsync += OnConnectionShutdownAsync;
        connection.ConnectionRecoveryErrorAsync += OnRecoveryErrorAsync;
        connection.RecoverySucceededAsync += OnRecoverySucceededAsync;
        Volatile.Write(ref _connection, connection);

        // the owner was disposed while the connection was being opened: whichever of the two takes it out closes it
        if (IsDisposed)
        {
            Close(Interlocked.Exchange(ref _connection, null));
            throw new ObjectDisposedException(nameof(RabbitPersistentConnection));
        }

        return connection;
    }

    /// <exception cref="InvalidOperationException">
    /// the connection is closed and has not been recovered, or this was disposed (<see cref="ObjectDisposedException"/>).
    /// </exception>
    public async Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);

        if (_lost || !connection.IsOpen)
            throw new InvalidOperationException("No RabbitMQ connections are available to perform this action: the connection is closed");

        return await connection.CreateChannelAsync(cancellationToken: cancellationToken);
    }
}
