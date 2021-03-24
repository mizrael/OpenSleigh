using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Transport.Kafka
{
    public record QueueReferences(string TopicName, string DeadLetterTopicName);
    
    public delegate QueueReferences QueueReferencesPolicy<TM>() where TM : IMessage;
}