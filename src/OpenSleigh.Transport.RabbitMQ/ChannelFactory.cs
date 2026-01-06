using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System.Collections.Concurrent;

namespace OpenSleigh.Transport.RabbitMQ;

public sealed class ChannelFactory : IChannelFactory, IAsyncDisposable
{
    private readonly IBusConnection _connection;

    private readonly SemaphoreSlim _semaphore;
    private readonly RabbitConfiguration _rabbitCfg;

    private IChannel? _publishChannel;
    private IChannel? _consumeChannel;

    public ChannelFactory(IBusConnection connection)
    {
        _semaphore = new SemaphoreSlim(1);
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public async ValueTask<IChannel> GetPublishChannelAsync(CancellationToken cancellationToken = default)
    {
        if (_publishChannel is not null && _publishChannel.IsOpen)
            return _publishChannel;

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (_publishChannel is not null && _publishChannel.IsOpen)
                return _publishChannel;

            _publishChannel?.Dispose();
            _publishChannel = await _connection.CreateChannelAsync(cancellationToken);
            return _publishChannel;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async ValueTask<IChannel> GetConsumeChannelAsync(CancellationToken cancellationToken = default)
    {
        if (_consumeChannel is not null && _consumeChannel.IsOpen)
            return _consumeChannel;

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (_consumeChannel is not null && _consumeChannel.IsOpen)
                return _consumeChannel;

            _consumeChannel?.Dispose();
            _consumeChannel = await _connection.CreateChannelAsync(cancellationToken);
            return _consumeChannel;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_publishChannel is not null)
        {
            if (_publishChannel.IsOpen)
                await _publishChannel.CloseAsync();
            _publishChannel.Dispose();
            _publishChannel = null;
        }

        if (_consumeChannel is not null)
        {
            if (_consumeChannel.IsOpen)
                await _consumeChannel.CloseAsync();
            _consumeChannel.Dispose();
            _consumeChannel = null;
        }
    }
}