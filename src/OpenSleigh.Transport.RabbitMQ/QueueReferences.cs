namespace OpenSleigh.Transport.RabbitMQ
{
    public record QueueReferences
    {
        public QueueReferences(string exchangeName, string queueName, string routingKey,
                               string deadLetterExchangeName, string deadLetterQueue)
        {
            if (string.IsNullOrWhiteSpace(exchangeName))
            {
                throw new ArgumentException($"'{nameof(exchangeName)}' cannot be null or whitespace.", nameof(exchangeName));
            }

            if (string.IsNullOrWhiteSpace(queueName))
            {
                throw new ArgumentException($"'{nameof(queueName)}' cannot be null or whitespace.", nameof(queueName));
            }

            if (string.IsNullOrWhiteSpace(deadLetterExchangeName))
            {
                throw new ArgumentException($"'{nameof(deadLetterExchangeName)}' cannot be null or whitespace.", nameof(deadLetterExchangeName));
            }

            if (string.IsNullOrWhiteSpace(deadLetterQueue))
            {
                throw new ArgumentException($"'{nameof(deadLetterQueue)}' cannot be null or whitespace.", nameof(deadLetterQueue));
            }

            ExchangeName = exchangeName;
            QueueName = queueName;
            RoutingKey = routingKey;
            DeadLetterExchangeName = deadLetterExchangeName;
            DeadLetterQueue = deadLetterQueue;
        }

        /// <summary>
        /// helper cTor, useful when creating standard messages (eg. commands). It's supposing that a single
        /// queue is bound to the exchange, hence no real need to use a custom routing key. The queue name will be used to
        /// route the message, just in case other queues get added to the same exchange
        /// </summary>
        public QueueReferences(string ExchangeName, string QueueName,
                                string DeadLetterExchangeName, string DeadLetterQueue) : this(ExchangeName, queueName: QueueName, routingKey: QueueName,
                                                                                              DeadLetterExchangeName, DeadLetterQueue){}

        public string RetryExchangeName => this.ExchangeName + ".retry";
        public string RetryQueueName => this.QueueName + ".retry";

        public string ExchangeName { get; }
        public string QueueName { get; }
        public string RoutingKey { get; }
        public string DeadLetterExchangeName { get; }
        public string DeadLetterQueue { get; }
    }
}