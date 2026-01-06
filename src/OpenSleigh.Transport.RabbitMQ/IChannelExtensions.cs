using RabbitMQ.Client;
using System.Collections.Concurrent;

namespace OpenSleigh.Transport.RabbitMQ;

public static class IChannelExtensions
{
    private static readonly ConcurrentDictionary<string, byte> _initializedExchanges = new();

    public static async ValueTask EnsureTopologyAsync(
        this IChannel channel, 
        QueueReferences queueReferences, 
        RabbitConfiguration rabbitCfg,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queueReferences);
        ArgumentNullException.ThrowIfNull(rabbitCfg);
        ArgumentNullException.ThrowIfNull(channel);

        // Declarations are idempotent; cache reduces redundant broker calls.
        if (_initializedExchanges.TryAdd(queueReferences.ExchangeName, 0))
        {
            await EnsureExchangesAsync(queueReferences, channel, cancellationToken);
            await EnsureQueuesAsync(queueReferences, channel, rabbitCfg, cancellationToken);
            return;
        }

        // Best-effort safety: ensure again in case the cache was populated before a complete initialization.
        await EnsureExchangesAsync(queueReferences, channel, cancellationToken);
        await EnsureQueuesAsync(queueReferences, channel, rabbitCfg, cancellationToken);
    }

    private static async ValueTask EnsureExchangesAsync(QueueReferences queueReferences, IChannel channel, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(exchange: queueReferences.ExchangeName, type: ExchangeType.Topic, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(exchange: queueReferences.DeadLetterExchangeName, type: ExchangeType.Topic, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(exchange: queueReferences.RetryExchangeName, type: ExchangeType.Topic, cancellationToken: cancellationToken);
    }

    private static async ValueTask EnsureQueuesAsync(QueueReferences queueReferences, IChannel channel, RabbitConfiguration rabbitCfg, CancellationToken cancellationToken)
    {
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

        await channel.QueueDeclareAsync(queue: queueReferences.RetryQueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
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

    public static async ValueTask DeleteAsync(this IChannel channel, QueueReferences queueRef)
    {
        await channel.ExchangeDeleteAsync(queueRef.ExchangeName);
        await channel.QueueDeleteAsync(queueRef.QueueName);

        await channel.ExchangeDeleteAsync(queueRef.DeadLetterExchangeName);
        await channel.QueueDeleteAsync(queueRef.DeadLetterQueue);

        await channel.ExchangeDeleteAsync(queueRef.RetryExchangeName);
        await channel.QueueDeleteAsync(queueRef.RetryQueueName);
    }
}