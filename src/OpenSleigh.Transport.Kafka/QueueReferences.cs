namespace OpenSleigh.Transport.Kafka;

public record QueueReferences(string TopicName, string DeadLetterTopicName);