namespace OpenSleigh.InMemory;

public record InMemorySagaOptions
{
    /// <summary>
    /// max size of the message batches processed concurrently by each subscriber.
    /// </summary>
    public int SubscriberMaxMessagesBatchSize { get; }

    /// <summary>
    /// how long a message waits before it is processed again after a lock conflict, that is when the saga instance
    /// it is for was being processed at the same time. Defaults to 100 milliseconds.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">the value is negative.</exception>
    public TimeSpan SubscriberRetryDelay
    {
        get => _subscriberRetryDelay;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            _subscriberRetryDelay = value;
        }
    }

    private readonly TimeSpan _subscriberRetryDelay = Messaging.InMemorySubscriberOptions.Defaults.RetryDelay;

    public InMemorySagaOptions(int messagesBatchSize)
    {
        SubscriberMaxMessagesBatchSize = messagesBatchSize;
    }

    public static readonly InMemorySagaOptions Defaults = new InMemorySagaOptions(5);
}
