using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System.Collections.Concurrent;

namespace OpenSleigh.Transport.RabbitMQ;

public sealed class ChannelFactory : IChannelFactory, IAsyncDisposable
{
    private readonly IBusConnection _connection;
    private readonly IReturnedMessageHandler? _returnedMessages;
    private readonly SemaphoreSlim _semaphore;

    private IChannel? _publishChannel;
    private IChannel? _consumeChannel;

    /// <param name="returnedMessages">
    /// what is told about the messages the broker sends back because no queue took them. Without it they are lost unnoticed.
    /// </param>
    public ChannelFactory(IBusConnection connection, IReturnedMessageHandler? returnedMessages = null)
    {
        _semaphore = new SemaphoreSlim(1);
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _returnedMessages = returnedMessages;
    }

    private void ListenForReturnedMessages(IChannel channel)
    {
        if (_returnedMessages is not { } returnedMessages)
            return;

        // not awaited: the channel waits for this to return, and what is done with the message uses the channel
        channel.BasicReturnAsync += (_, returned) =>
        {
            _ = returnedMessages.HandleAsync(channel, returned);
            return Task.CompletedTask;
        };
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
            ListenForReturnedMessages(_publishChannel);
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

        _semaphore?.Dispose();
    }
}