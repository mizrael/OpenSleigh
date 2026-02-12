using RabbitMQ.Client;
using System.Collections.Concurrent;

namespace OpenSleigh.Transport.RabbitMQ;

public static class IChannelExtensions
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();
    private static readonly ConcurrentDictionary<string, byte> _initialized = new();

    public static async ValueTask EnsureTopologyAsync(
        this IChannel channel,
        QueueReferences queueReferences,
        RabbitConfiguration rabbitCfg,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queueReferences);
        ArgumentNullException.ThrowIfNull(rabbitCfg);
        ArgumentNullException.ThrowIfNull(channel);

        var topologyKey = $"{queueReferences.ExchangeName}|{queueReferences.QueueName}";
        if (_initialized.ContainsKey(topologyKey))
            return;

        var semaphore = _semaphores.GetOrAdd(topologyKey, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized.ContainsKey(topologyKey))
                return;

            await EnsureExchangesAsync(queueReferences, channel, rabbitCfg, cancellationToken);
            await EnsureQueuesAsync(queueReferences, channel, rabbitCfg, cancellationToken);

            _initialized.TryAdd(topologyKey, 0);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static async ValueTask EnsureExchangesAsync(QueueReferences queueReferences, IChannel channel, RabbitConfiguration rabbitCfg, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(
            exchange: queueReferences.ExchangeName,
            type: ExchangeType.Topic,
            durable: rabbitCfg.Durable,
            autoDelete: rabbitCfg.AutoDelete,
            cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(
           exchange: queueReferences.DeadLetterExchangeName,
           type: ExchangeType.Topic,
           durable: rabbitCfg.Durable,
           autoDelete: rabbitCfg.AutoDelete,
           cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(
           exchange: queueReferences.RetryExchangeName,
           type: ExchangeType.Topic,
           durable: rabbitCfg.Durable,
           autoDelete: rabbitCfg.AutoDelete,
           cancellationToken: cancellationToken);
    }

    private static async ValueTask EnsureQueuesAsync(QueueReferences queueReferences, IChannel channel, RabbitConfiguration rabbitCfg, CancellationToken cancellationToken)
    {
        await channel.QueueDeclareAsync(queue: queueReferences.DeadLetterQueue,
              durable: rabbitCfg.Durable,
              exclusive: false,
              autoDelete: rabbitCfg.AutoDelete,
              arguments: null,
              cancellationToken: cancellationToken);
        await channel.QueueBindAsync(queueReferences.DeadLetterQueue,
                          queueReferences.DeadLetterExchangeName,
                          routingKey: queueReferences.DeadLetterQueue,
                          arguments: null,
                          cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(queue: queueReferences.RetryQueueName,
                durable: rabbitCfg.Durable,
                exclusive: false,
                autoDelete: rabbitCfg.AutoDelete,
                arguments: new Dictionary<string, object?>()
                {
                    {Headers.XMessageTTL, (int)rabbitCfg.RetryDelay.TotalMilliseconds },
                    {Headers.XDeadLetterExchange, queueReferences.ExchangeName},
                    {Headers.XDeadLetterRoutingKey, queueReferences.RoutingKey}
                }, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(queue: queueReferences.RetryQueueName,
            exchange: queueReferences.RetryExchangeName,
            routingKey: queueReferences.RoutingKey,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(queue: queueReferences.QueueName,
                durable: rabbitCfg.Durable,
                exclusive: false,
                autoDelete: rabbitCfg.AutoDelete,
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

    public static async ValueTask DeleteAsync(this IChannel channel, QueueReferences queueRef)
    {
        await channel.ExchangeDeleteAsync(queueRef.ExchangeName);
        await channel.QueueDeleteAsync(queueRef.QueueName);

        await channel.ExchangeDeleteAsync(queueRef.DeadLetterExchangeName);
        await channel.QueueDeleteAsync(queueRef.DeadLetterQueue);

        await channel.ExchangeDeleteAsync(queueRef.RetryExchangeName);
        await channel.QueueDeleteAsync(queueRef.RetryQueueName);

        var topologyKey = $"{queueRef.ExchangeName}|{queueRef.QueueName}";
        _initialized.TryRemove(topologyKey, out _);
        _semaphores.TryRemove(topologyKey, out _);
    }
}