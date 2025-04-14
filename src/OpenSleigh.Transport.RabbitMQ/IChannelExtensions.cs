using OpenSleigh.Transport.RabbitMQ;
using RabbitMQ.Client;

public static class IChannelExtensions
{
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