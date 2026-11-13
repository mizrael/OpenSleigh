namespace OpenSleigh.Transport.RabbitMQ;

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

    /// <summary>
    /// this will be used to delay the message before re-sending it to the main queue
    /// </summary>
    public string RetryExchangeName => this.ExchangeName + ".retry";

    /// <summary>
    /// the queue where a message waits before it is processed again. It sends the message back to <see cref="QueueName"/>
    /// only, not to every queue bound to the exchange with the same routing key.
    /// </summary>
    /// <remarks>
    /// A queue cannot be told to send its messages somewhere else once it exists. When the routing key is shared by
    /// several queues, as with the default naming, the retry queue of version 3.3.0 and earlier sends a message back
    /// to all of them: another queue takes its place, and the old one (<see cref="LegacyRetryQueueName"/>) is left
    /// to deliver what it still holds.
    /// </remarks>
    public string RetryQueueName => ReturnsToOwnQueueOnly ? LegacyRetryQueueName : this.QueueName + ".retry.direct";

    internal string LegacyRetryQueueName => this.QueueName + ".retry";

    private bool ReturnsToOwnQueueOnly => string.Equals(this.RoutingKey, this.QueueName, StringComparison.Ordinal);

    public string ExchangeName { get; }
    public string QueueName { get; }
    public string RoutingKey { get; }
    public string DeadLetterExchangeName { get; }
    public string DeadLetterQueue { get; }
}