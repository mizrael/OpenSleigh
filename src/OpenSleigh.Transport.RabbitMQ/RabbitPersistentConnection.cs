
using Microsoft.Extensions.Logging;
using Polly;
using RabbitMQ.Client;

namespace OpenSleigh.Transport.RabbitMQ;

public sealed class RabbitPersistentConnection : IDisposable, IBusConnection
{
    private readonly ILogger<RabbitPersistentConnection> _logger;
    private readonly IConnectionFactory _connectionFactory;
    private IConnection? _connection;
    private bool _disposed;

    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public RabbitPersistentConnection(IConnectionFactory connectionFactory, ILogger<RabbitPersistentConnection> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsConnected => _connection != null && _connection.IsOpen && !_disposed;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _connection?.Dispose();
    }

    private async Task TryConnectAsync(CancellationToken cancellationToken = default)
    {
        _semaphore.Wait(cancellationToken);

        try
        {
            await TryConnectCoreAsync(cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async ValueTask TryConnectCoreAsync(CancellationToken cancellationToken)
    {
        if (IsConnected)
            return;

        var policy = Policy
                    .Handle<Exception>()
                    .WaitAndRetryAsync(5,
                        retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                        (ex, timeSpan, context) =>
                        {
                            _logger.LogError(ex, $"an exception has occurred while opening RabbitMQ connection: {ex.Message}");
                        });

        _connection = await policy.ExecuteAsync(_connectionFactory.CreateConnectionAsync, cancellationToken);

        _connection.ConnectionShutdownAsync += async (s, e) => await TryConnectAsync();
        _connection.CallbackExceptionAsync += async (s, e) => await TryConnectAsync();
        _connection.ConnectionShutdownAsync += async (s, e) => await TryConnectAsync();
    }

    public async Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken)
    {
        await TryConnectAsync(cancellationToken);

        if (!IsConnected || _connection is null)
            throw new InvalidOperationException("No RabbitMQ connections are available to perform this action");

        var channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
        return channel;
    }

}
