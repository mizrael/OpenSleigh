namespace OpenSleigh.InMemory.Messaging
{
    public record InMemorySubscriberOptions
    {
        /// <summary>
        /// max size of the message batches processed concurrently by each subscriber.
        /// </summary>
        public int MaxMessagesBatchSize { get; }

        public InMemorySubscriberOptions(int messagesBatchSize)
        {
            MaxMessagesBatchSize = messagesBatchSize;
        }

        public static readonly InMemorySubscriberOptions Defaults = new InMemorySubscriberOptions(5);
    }
}