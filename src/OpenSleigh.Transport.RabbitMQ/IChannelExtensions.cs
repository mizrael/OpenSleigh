using RabbitMQ.Client;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace OpenSleigh.Transport.RabbitMQ;

public static class IChannelExtensions
{
    // what was declared, by the channel it was declared on: another channel can be on another broker or virtual host,
    // and a channel that replaces a closed one declares again what a restarted broker may have lost
    private static readonly ConditionalWeakTable<IChannel, DeclaredTopology> _declared = new();

    private sealed class DeclaredTopology
    {
        /// <summary>exchange and queue of a whole topology, or an exchange alone, with no queue.</summary>
        public ConcurrentDictionary<(string ExchangeName, string? QueueName), byte> Done { get; } = new();

        public SemaphoreSlim Gate { get; } = new(1, 1);
    }

    public static async ValueTask EnsureTopologyAsync(
        this IChannel channel,
        QueueReferences queueReferences,
        RabbitConfiguration rabbitCfg,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queueReferences);
        ArgumentNullException.ThrowIfNull(rabbitCfg);
        ArgumentNullException.ThrowIfNull(channel);

        var declared = _declared.GetOrCreateValue(channel);
        var key = (queueReferences.ExchangeName, (string?)queueReferences.QueueName);
        if (declared.Done.ContainsKey(key))
            return;

        await EnsureOnceAsync(declared, key, async () =>
        {
            await EnsureExchangesAsync(queueReferences, channel, rabbitCfg, cancellationToken);
            await EnsureQueuesAsync(queueReferences, channel, rabbitCfg, cancellationToken);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// declares the exchange a message is published to, and nothing else: what a host needs to publish a message
    /// it does not consume. The queues belong to the services that consume the message, which declare their own.
    /// </summary>
    public static ValueTask EnsureExchangeAsync(
        this IChannel channel,
        QueueReferences queueReferences,
        RabbitConfiguration rabbitCfg,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queueReferences);
        ArgumentNullException.ThrowIfNull(rabbitCfg);
        ArgumentNullException.ThrowIfNull(channel);

        return EnsureExchangeOnceAsync(channel, queueReferences.ExchangeName, rabbitCfg, cancellationToken);
    }

    /// <summary>
    /// declares the exchange the dead letters of a message go to, and no queue: what a host needs to dead-letter
    /// a message it does not consume.
    /// </summary>
    internal static ValueTask EnsureDeadLetterExchangeAsync(
        this IChannel channel,
        QueueReferences queueReferences,
        RabbitConfiguration rabbitCfg,
        CancellationToken cancellationToken = default)
        => EnsureExchangeOnceAsync(channel, queueReferences.DeadLetterExchangeName, rabbitCfg, cancellationToken);

    private static async ValueTask EnsureExchangeOnceAsync(IChannel channel, string exchangeName, RabbitConfiguration rabbitCfg, CancellationToken cancellationToken)
    {
        var declared = _declared.GetOrCreateValue(channel);
        var key = (exchangeName, (string?)null);
        if (declared.Done.ContainsKey(key))
            return;

        await EnsureOnceAsync(
            declared,
            key,
            () => EnsureExchangeAsync(exchangeName, channel, rabbitCfg, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask EnsureOnceAsync(
        DeclaredTopology declared,
        (string ExchangeName, string? QueueName) key,
        Func<ValueTask> declare,
        CancellationToken cancellationToken)
    {
        await declared.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (declared.Done.ContainsKey(key))
                return;

            await declare().ConfigureAwait(false);

            declared.Done.TryAdd(key, 0);
        }
        finally
        {
            declared.Gate.Release();
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

        // only this channel is told: the others declare again what they find missing when they are replaced
        if (_declared.TryGetValue(channel, out var declared))
        {
            declared.Done.TryRemove((queueRef.ExchangeName, queueRef.QueueName), out _);
            declared.Done.TryRemove((queueRef.ExchangeName, null), out _);
            declared.Done.TryRemove((queueRef.DeadLetterExchangeName, null), out _);
        }
    }
}