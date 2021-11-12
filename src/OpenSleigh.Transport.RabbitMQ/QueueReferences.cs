namespace OpenSleigh.Transport.RabbitMQ
{
    public record QueueReferences(string ExchangeName, string QueueName, string RoutingKey,
                                    string DeadLetterExchangeName, string DeadLetterQueue)
    {
        /// <summary>
        /// helper cTor, useful when creating standard messages (eg. commands). It's supposing that a single
        /// queue is bound to the exchange, hence no real need to use a custom routing key. The queue name will be used to
        /// route the message, just in case other queues get added to the same exchange
        /// </summary>
        public QueueReferences(string ExchangeName, string QueueName,
                                string DeadLetterExchangeName, string DeadLetterQueue) : this(ExchangeName, QueueName: QueueName, RoutingKey: QueueName,
                                                                                              DeadLetterExchangeName, DeadLetterQueue){}

        public string RetryExchangeName => this.ExchangeName + ".retry";
        public string RetryQueueName => this.QueueName + ".retry";
    }
}