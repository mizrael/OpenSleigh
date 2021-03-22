using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OpenSleigh.Persistence.Cosmos.SQL.Entities
{
    public record OutboxMessage(Guid Id, byte[] Data, string Type)
    {
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

        public static OutboxMessage New(Guid id, byte[] data, string type, Guid correlationId) => new OutboxMessage(id, data, type)
        {
            Status = MessageStatuses.Pending,
            PartitionKey = correlationId.ToString()
        };

        public enum MessageStatuses
        {
            Pending,
            Processed
        }
    }

    internal class OutboxMessageStateEntityTypeConfiguration : IEntityTypeConfiguration<OutboxMessage>
    {
        public void Configure(EntityTypeBuilder<OutboxMessage> builder)
        {
            builder.ToContainer("OutboxMessages")
                .HasNoDiscriminator();

            builder.HasKey(e => e.Id);
            builder.HasPartitionKey(e => e.PartitionKey);
        }
    }
}