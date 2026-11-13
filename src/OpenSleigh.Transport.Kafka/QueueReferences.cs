namespace OpenSleigh.Transport.Kafka;

/// <param name="TopicName">the topic the messages of a type are published to and consumed from.</param>
/// <param name="DeadLetterTopicName">the topic that receives the messages that could not be processed.</param>
public record QueueReferences(string TopicName, string DeadLetterTopicName)
{
    private const string RetryTopicSuffix = ".retry";

    /// <summary>
    /// the topic that holds the messages waiting to be processed again, after a lock conflict.
    /// Defaults to the topic name followed by ".retry".
    /// </summary>
    public string RetryTopicName { get; init; } = TopicName + RetryTopicSuffix;
}