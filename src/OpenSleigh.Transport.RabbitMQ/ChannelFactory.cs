using Microsoft.Extensions.Logging;
using OpenSleigh.Outbox;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OpenSleigh.Transport.RabbitMQ;

public sealed class ChannelFactory : IChannelFactory, IAsyncDisposable
{
    private readonly IBusConnection _connection;
    private readonly ILogger<ChannelFactory>? _logger;
    private readonly IReturnedMessageHandler? _returnedMessages;
    private readonly SemaphoreSlim _semaphore;

    private IChannel? _publishChannel;
    private IChannel? _consumeChannel;

    /// <param name="logger">
    /// where the messages the broker sends back, because no queue took them, are reported when nothing handles them.
    /// </param>
    /// <param name="returnedMessages">
    /// what is given the messages the broker sends back. Without it they are dropped, and only reported.
    /// </param>
    public ChannelFactory(IBusConnection connection, ILogger<ChannelFactory>? logger = null, IReturnedMessageHandler? returnedMessages = null)
    {
        _semaphore = new SemaphoreSlim(1);
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _logger = logger;
        _returnedMessages = returnedMessages;
    }

    private void ListenForReturnedMessages(IChannel channel)
    {
        if (_returnedMessages is { } returnedMessages)
            channel.BasicReturnAsync += (_, returned) =>
            {
                returnedMessages.Accept(returned);
                return Task.CompletedTask;
            };
        else if (_logger is not null)
            channel.BasicReturnAsync += (_, returned) =>
            {
                LogReturned(_logger, returned);
                return Task.CompletedTask;
            };
    }

    // the channel waits for this to return: it reads four values and writes one log entry, nothing else
    private static void LogReturned(ILogger logger, BasicReturnEventArgs returned)
    {
        if (!logger.IsEnabled(LogLevel.Warning))
            return;

        returned.BasicProperties.TryGetHeaderValue(nameof(MessageEnvelope.MessageType), out var messageType);

        logger.LogWarning(
            "message '{MessageId}' of type '{MessageType}' was returned by the broker from Exchange '{ExchangeName}' with '{ReplyText}': " +
            "no queue took it, and it is dropped. No service that consumes it had declared its queue when it was published.",
            returned.BasicProperties.MessageId, messageType, returned.Exchange, returned.ReplyText);
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

            _publishChannel = await ReplaceAsync(_publishChannel, cancellationToken);
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

            _consumeChannel = await ReplaceAsync(_consumeChannel, cancellationToken);
            return _consumeChannel;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    // the new channel first: without a connection it fails, and the closed channel is left as it is for the client
    // to recover with the connection. Disposed, it would not be recovered
    private async ValueTask<IChannel> ReplaceAsync(IChannel? closed, CancellationToken cancellationToken)
    {
        var channel = await _connection.CreateChannelAsync(cancellationToken);
        closed?.Dispose();
        return channel;
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