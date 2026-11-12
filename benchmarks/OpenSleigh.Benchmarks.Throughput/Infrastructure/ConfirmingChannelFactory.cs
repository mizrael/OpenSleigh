using OpenSleigh.Transport.RabbitMQ;
using RabbitMQ.Client;

namespace OpenSleigh.Benchmarks.Throughput.Infrastructure;

/// <summary>
/// What an application can register today in place of the library's channel factory to get publisher confirms:
/// the publish channel waits for the broker to confirm every message, the consume channel is as the library makes it.
/// </summary>
internal sealed class ConfirmingChannelFactory : IChannelFactory, IAsyncDisposable
{
    private const int MaxOutstandingConfirms = 256;

    private readonly IConnectionFactory _connectionFactory;
    private readonly CreateChannelOptions _publishOptions;
    private readonly CreateChannelOptions _consumeOptions;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IConnection? _connection;
    private IChannel? _publishChannel;
    private IChannel? _consumeChannel;

    public ConfirmingChannelFactory(IConnectionFactory connectionFactory, RabbitConfiguration configuration)
    {
        _connectionFactory = connectionFactory;

        _publishOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true,
            outstandingPublisherConfirmationsRateLimiter: new ThrottlingRateLimiter(MaxOutstandingConfirms),
            consumerDispatchConcurrency: 1);

        // the default of this argument is 1, not the value set on the connection factory
        _consumeOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: false,
            publisherConfirmationTrackingEnabled: false,
            outstandingPublisherConfirmationsRateLimiter: null,
            consumerDispatchConcurrency: configuration.ConsumerDispatchConcurrency);
    }

    public async ValueTask<IChannel> GetPublishChannelAsync(CancellationToken cancellationToken = default)
        => _publishChannel is { IsOpen: true }
            ? _publishChannel
            : _publishChannel = await OpenAsync(() => _publishChannel, _publishOptions, cancellationToken);

    public async ValueTask<IChannel> GetConsumeChannelAsync(CancellationToken cancellationToken = default)
        => _consumeChannel is { IsOpen: true }
            ? _consumeChannel
            : _consumeChannel = await OpenAsync(() => _consumeChannel, _consumeOptions, cancellationToken);

    private async Task<IChannel> OpenAsync(Func<IChannel?> current, CreateChannelOptions options, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (current() is { IsOpen: true } open)
                return open;

            _connection ??= await _connectionFactory.CreateConnectionAsync(cancellationToken);
            return await _connection.CreateChannelAsync(options, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
        _gate.Dispose();
    }
}
