namespace OpenSleigh.Transport.RabbitMQ
{
    public record QueueReferences(string ExchangeName, string QueueName, string RoutingKey,
                                    string DeadLetterExchangeName, string DeadLetterQueue)
    {
        public QueueReferences(string ExchangeName, string QueueName,
                                string DeadLetterExchangeName, string DeadLetterQueue) : this(ExchangeName, QueueName: QueueName, RoutingKey: QueueName,
                                                                                              DeadLetterExchangeName, DeadLetterQueue){}
    }
}