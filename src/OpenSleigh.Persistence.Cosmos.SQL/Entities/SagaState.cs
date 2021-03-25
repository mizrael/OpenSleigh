using System;
using OpenSleigh.Core.Exceptions;

namespace OpenSleigh.Persistence.Cosmos.SQL.Entities
{
    public class SagaState
    {
        private SagaState() { }
        private SagaState(Guid correlationId, string type)
        {
            PartitionKey = correlationId.ToString();
            CorrelationId = correlationId;
            Type = type;
        }

        public string PartitionKey { get; init; }
        public Guid CorrelationId { get; }
        public string Type { get; }

        public byte[] Data { get; private set; }
        public Guid? LockId { get; private set; }
        public DateTime? LockTime { get; private set; }

        public void Lock(byte[] data)
        {
            this.Data = data;
            this.LockId = Guid.NewGuid();
            this.LockTime = DateTime.UtcNow;
        }

        public void RefreshLock()
        {
            this.LockId = Guid.NewGuid();
            this.LockTime = DateTime.UtcNow;
        }

        public void Release(byte[] data)
        {
            this.LockTime = null;
            this.LockId = null;
            this.Data = data;
        }

        public static SagaState New(Guid correlationId, string type)
            => new SagaState(correlationId, type);
    }
}
