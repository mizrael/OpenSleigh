using System;

namespace OpenSleigh.Persistence.Cosmos.SQL.Entities
{
    public class OutboxMessage
    {
        private OutboxMessage()
        {
        }

        public Guid Id { get; init; }
        public ReadOnlyMemory<byte> Data { get; init; }
        public string Type { get; init; }
        public string PartitionKey { get; init; }

        public MessageStatuses Status { get; private set; }
        public Guid? LockId { get; private set; }
        public DateTime? LockTime { get; private set; }
        public DateTime? PublishingDate { get; private set; }

        public void Lock()
        {
            this.LockId = Guid.NewGuid();
            this.LockTime = DateTime.UtcNow;
        }

        public void Release()
        {
            this.LockId = null;
            this.LockTime = null;
            this.PublishingDate = DateTime.UtcNow;
            this.Status = OutboxMessage.MessageStatuses.Processed;
        }

        public static OutboxMessage New(Guid id, ReadOnlyMemory<byte> data, string type, Guid correlationId) => new OutboxMessage
        {
            Id = id,
            Data = data,
            Type = type,
            Status = MessageStatuses.Pending,
            PartitionKey = correlationId.ToString()
        };

        public enum MessageStatuses
        {
            Pending,
            Processed
        }
    }
}