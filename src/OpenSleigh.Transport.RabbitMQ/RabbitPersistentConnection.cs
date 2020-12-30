using Microsoft.Extensions.Logging;
using Polly;
using RabbitMQ.Client;
using System;

namespace OpenSleigh.Transport.RabbitMQ
{
    public class RabbitPersistentConnection : IDisposable, IBusConnection
    {
        private readonly ILogger<RabbitPersistentConnection> _logger;
        private readonly IConnectionFactory _connectionFactory;
        private IConnection _connection;
        private bool _disposed;

        private readonly object semaphore = new object();

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

        private void TryConnect()
        {
            lock (semaphore)
            {
                if (IsConnected)
                    return;

                var policy = Policy
                                .Handle<Exception>()
                                .WaitAndRetry(5,
                                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                                    (ex, timeSpan, context) =>
                                    {
                                        _logger.LogError(ex, $"an exception has occurred while opening RabbitMQ connection: {ex.Message}");
                                    });

                _connection = policy.Execute(_connectionFactory.CreateConnection);

                _connection.ConnectionShutdown += (s, e) => TryConnect();
                _connection.CallbackException += (s, e) => TryConnect();
                _connection.ConnectionBlocked += (s, e) => TryConnect();
            }
        }

        public IModel CreateChannel()
        {
            TryConnect();

            if (!IsConnected)
                throw new InvalidOperationException("No RabbitMQ connections are available to perform this action");

            return _connection.CreateModel();
        }

    }
}
