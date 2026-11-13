namespace OpenSleigh.InMemory.Messaging;

public record InMemorySubscriberOptions
{
    /// <summary>
    /// max size of the message batches processed concurrently by each subscriber.
    /// </summary>
    public int MaxMessagesBatchSize { get; }

    public TimeSpan PollingInterval { get; }

    /// <summary>
    /// how long a message waits before it is processed again after a lock conflict, that is when the saga instance
    /// it is for was being processed at the same time. Defaults to 100 milliseconds.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">the value is negative.</exception>
    public TimeSpan RetryDelay
    {
        get => _retryDelay;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            _retryDelay = value;
        }
    }

    private readonly TimeSpan _retryDelay = TimeSpan.FromMilliseconds(100);

    public InMemorySubscriberOptions(int messagesBatchSize, TimeSpan pollingInterval)
    {
        MaxMessagesBatchSize = messagesBatchSize;
        PollingInterval = pollingInterval;
    }

    public static readonly InMemorySubscriberOptions Defaults = new InMemorySubscriberOptions(5, TimeSpan.FromMilliseconds(200));
}