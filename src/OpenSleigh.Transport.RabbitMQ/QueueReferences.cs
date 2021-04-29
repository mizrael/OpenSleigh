namespace OpenSleigh.Transport.RabbitMQ
{
    public record QueueReferences(string ExchangeName, string QueueName, string RoutingKey, string DeadLetterExchangeName, string DeadLetterQueue);
}