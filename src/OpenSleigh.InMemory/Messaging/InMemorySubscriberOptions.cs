namespace OpenSleigh.InMemory.Messaging;

public record InMemorySubscriberOptions
{
    /// <summary>
    /// max size of the message batches processed concurrently by each subscriber.
    /// </summary>
    public int MaxMessagesBatchSize { get; }

    public TimeSpan PollingInterval { get; }

    public InMemorySubscriberOptions(int messagesBatchSize, TimeSpan pollingInterval)
    {
        MaxMessagesBatchSize = messagesBatchSize;
        PollingInterval = pollingInterval;
    }

    public static readonly InMemorySubscriberOptions Defaults = new InMemorySubscriberOptions(5, TimeSpan.FromMilliseconds(200));
}