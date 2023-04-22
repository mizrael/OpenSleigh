namespace OpenSleigh.InMemory
{
    public record InMemorySagaOptions
    {
        /// <summary>
        /// max size of the message batches processed concurrently by each subscriber.
        /// </summary>
        public int SubscriberMaxMessagesBatchSize { get; }

        public InMemorySagaOptions(int messagesBatchSize)
        {
            SubscriberMaxMessagesBatchSize = messagesBatchSize;
        }

        public static readonly InMemorySagaOptions Defaults = new InMemorySagaOptions(5);
    }
}
