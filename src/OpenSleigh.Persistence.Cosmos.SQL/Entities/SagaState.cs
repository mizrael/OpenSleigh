using System;

namespace OpenSleigh.Persistence.Cosmos.SQL.Entities
{
    public record SagaState(string PartitionKey, Guid CorrelationId, string Type)
    {
        public byte[] Data { get; set; } = null;
        public Guid? LockId { get; set; } = null;
        public DateTime? LockTime { get; set; } = null;
    }

}
