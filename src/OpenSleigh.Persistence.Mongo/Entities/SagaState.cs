using MongoDB.Bson;

namespace OpenSleigh.Persistence.Mongo.Entities
{
    public class SagaState
    {
        public required ObjectId Id { get; init; }
        public required string CorrelationId { get; init; }
        public required string InstanceId { get; init; }
        public required string TriggerMessageId { get; init; }
        public required string SagaType { get; init; }
        public required string? SagaStateType { get; init; }
        public byte[]? StateData { get; set; }
        public bool IsCompleted { get; set; } 

        public ICollection<SagaProcessedMessage> ProcessedMessages { get; init; } = new HashSet<SagaProcessedMessage>();

        public string? LockId { get; set; }
        public DateTimeOffset? LockTime { get; set; }
    }
}
