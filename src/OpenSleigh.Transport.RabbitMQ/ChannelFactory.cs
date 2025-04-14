using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System.Collections.Concurrent;

namespace OpenSleigh.Transport.RabbitMQ;

public sealed class ChannelFactory : IChannelFactory, IAsyncDisposable
{
    private readonly IBusConnection _connection;
    private readonly ConcurrentDictionary<string, IChannel> _channelsByExchange = new ();
    private readonly SemaphoreSlim _semaphore;
    private readonly RabbitConfiguration _rabbitCfg;
    private readonly ILogger<ChannelFactory> _logger;

    public ChannelFactory(IBusConnection connection, RabbitConfiguration rabbitCfg, ILogger<ChannelFactory> logger)
    {
        _semaphore = new SemaphoreSlim (1);
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rabbitCfg = rabbitCfg ?? throw new ArgumentNullException(nameof(rabbitCfg));
    }

    public async ValueTask<IChannel> GetAsync(QueueReferences queueReferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queueReferences, nameof(queueReferences));

        if (_channelsByExchange.TryGetValue(queueReferences.ExchangeName, out var channel))
            return channel;

        await _semaphore.WaitAsync();
        
        try
        {
            if (_channelsByExchange.TryGetValue(queueReferences.ExchangeName, out channel))
                return channel;

            channel = await _connection.CreateChannelAsync(cancellationToken);
            await EnsureExchangesAsync(queueReferences, channel, cancellationToken);
            await EnsureQueuesAsync(queueReferences, channel, cancellationToken);

            _channelsByExchange.TryAdd(queueReferences.ExchangeName, channel);

            return channel;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private static async Task EnsureExchangesAsync(QueueReferences queueReferences, IChannel channel, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(exchange: queueReferences.DeadLetterExchangeName, type: ExchangeType.Topic, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(exchange: queueReferences.RetryExchangeName, type: ExchangeType.Topic, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(exchange: queueReferences.ExchangeName, type: ExchangeType.Topic, cancellationToken: cancellationToken);
    }

    private async Task EnsureQueuesAsync(QueueReferences queueReferences, IChannel channel, CancellationToken cancellationToken)
    {
        _logger.LogInformation($"initializing dead-letter queue '{queueReferences.DeadLetterQueue}' on exchange '{queueReferences.DeadLetterExchangeName}'...");
        
        await channel.QueueDeclareAsync(queue: queueReferences.DeadLetterQueue,
              durable: true,
              exclusive: false,
              autoDelete: false,
              arguments: null,
              cancellationToken: cancellationToken);
        await channel.QueueBindAsync(queueReferences.DeadLetterQueue,
                          queueReferences.DeadLetterExchangeName,
                          routingKey: queueReferences.DeadLetterQueue,
                          arguments: null,
                          cancellationToken: cancellationToken);
       
        _logger.LogInformation($"initializing retry queue '{queueReferences.RetryQueueName}' on exchange '{queueReferences.RetryExchangeName}'...");
        await channel.QueueDeclareAsync(queue: queueReferences.RetryQueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object?>()
                {
                    {Headers.XMessageTTL, (int)_rabbitCfg.RetryDelay.TotalMilliseconds },
                    {Headers.XDeadLetterExchange, queueReferences.ExchangeName},
                    {Headers.XDeadLetterRoutingKey, queueReferences.RoutingKey}
                }, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(queue: queueReferences.RetryQueueName,
            exchange: queueReferences.RetryExchangeName,
            routingKey: queueReferences.RoutingKey,
            arguments: null,
            cancellationToken: cancellationToken);

        _logger.LogInformation($"initializing queue '{queueReferences.QueueName}' on exchange '{queueReferences.ExchangeName}'...");
        await channel.QueueDeclareAsync(queue: queueReferences.QueueName,
               durable: true,
               exclusive: false,
               autoDelete: false,
               arguments: new Dictionary<string, object?>()
               {
                    {Headers.XDeadLetterExchange, queueReferences.DeadLetterExchangeName},
                    {Headers.XDeadLetterRoutingKey, queueReferences.DeadLetterQueue}
               },
               cancellationToken: cancellationToken);

        await channel.QueueBindAsync(queue: queueReferences.QueueName,
            exchange: queueReferences.ExchangeName,
            routingKey: queueReferences.RoutingKey,
            arguments: null,
            cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (KeyValuePair<string, IChannel> kv in _channelsByExchange)
        {
            if (kv.Value.IsOpen)
                await kv.Value.CloseAsync();
            kv.Value.Dispose();
        }
        _channelsByExchange.Clear();
    }
}