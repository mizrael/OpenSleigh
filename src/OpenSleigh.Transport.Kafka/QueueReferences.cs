using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Transport.Kafka
{
    public record QueueReferences(string TopicName);

    public delegate QueueReferences QueueReferencesPolicy<TM>() where TM : IMessage;
}