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

        await EnsureOnceAsync(topologyKey, async () =>
        {
            await EnsureExchangesAsync(queueReferences, channel, rabbitCfg, cancellationToken);
            await EnsureQueuesAsync(queueReferences, channel, rabbitCfg, cancellationToken);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// declares the exchange a message is published to, and nothing else: what a host needs to publish a message
    /// it does not consume. The queues belong to the services that consume the message, which declare their own.
    /// </summary>
    public static async ValueTask EnsureExchangeAsync(
        this IChannel channel,
        QueueReferences queueReferences,
        RabbitConfiguration rabbitCfg,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queueReferences);
        ArgumentNullException.ThrowIfNull(rabbitCfg);
        ArgumentNullException.ThrowIfNull(channel);

        var exchangeName = queueReferences.ExchangeName;
        if (_initialized.ContainsKey(exchangeName))
            return;

        await EnsureOnceAsync(
            exchangeName,
            () => EnsureExchangeAsync(exchangeName, channel, rabbitCfg, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask EnsureOnceAsync(string key, Func<ValueTask> declare, CancellationToken cancellationToken)
    {
        var semaphore = _semaphores.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized.ContainsKey(key))
                return;

            await declare().ConfigureAwait(false);

            _initialized.TryAdd(key, 0);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static async ValueTask EnsureExchangeAsync(string exchangeName, IChannel channel, RabbitConfiguration rabbitCfg, CancellationToken cancellationToken)
        => await channel.ExchangeDeclareAsync(
            exchange: exchangeName,
            type: ExchangeType.Topic,
            durable: rabbitCfg.Durable,
            autoDelete: rabbitCfg.AutoDelete,
            cancellationToken: cancellationToken);

    private static async ValueTask EnsureExchangesAsync(QueueReferences queueReferences, IChannel channel, RabbitConfiguration rabbitCfg, CancellationToken cancellationToken)
    {
        await EnsureExchangeAsync(queueReferences.ExchangeName, channel, rabbitCfg, cancellationToken);
        await EnsureExchangeAsync(queueReferences.DeadLetterExchangeName, channel, rabbitCfg, cancellationToken);
        await EnsureExchangeAsync(queueReferences.RetryExchangeName, channel, rabbitCfg, cancellationToken);
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

        // a message that waited for its retry goes back to the queue it came from, by the name of that queue:
        // with the routing key of the message type it would reach every queue bound with it, one per service
        await channel.QueueDeclareAsync(queue: queueReferences.RetryQueueName,
                durable: rabbitCfg.Durable,
                exclusive: false,
                autoDelete: rabbitCfg.AutoDelete,
                arguments: new Dictionary<string, object?>()
                {
                    {Headers.XMessageTTL, (int)rabbitCfg.RetryDelay.TotalMilliseconds },
                    {Headers.XDeadLetterExchange, queueReferences.ExchangeName},
                    {Headers.XDeadLetterRoutingKey, queueReferences.QueueName}
                }, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(queue: queueReferences.RetryQueueName,
            exchange: queueReferences.RetryExchangeName,
            routingKey: queueReferences.RetryQueueName,
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

        if (queueReferences.QueueName != queueReferences.RoutingKey)
            await channel.QueueBindAsync(queue: queueReferences.QueueName,
                exchange: queueReferences.ExchangeName,
                routingKey: queueReferences.QueueName,
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
        await channel.QueueDeleteAsync(queueRef.LegacyRetryQueueName);

        var topologyKey = $"{queueRef.ExchangeName}|{queueRef.QueueName}";
        _initialized.TryRemove(topologyKey, out _);
        _semaphores.TryRemove(topologyKey, out _);
        _initialized.TryRemove(queueRef.ExchangeName, out _);
        _semaphores.TryRemove(queueRef.ExchangeName, out _);
    }
}