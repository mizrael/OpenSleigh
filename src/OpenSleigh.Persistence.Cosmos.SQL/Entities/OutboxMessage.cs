using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OpenSleigh.Persistence.Cosmos.SQL.Entities
{
    public record OutboxMessage(Guid Id, byte[] Data, string Type, string PartitionKey)
    {
        public string Status { get; set; }
        public Guid? LockId { get; set; }
        public DateTime? LockTime { get; set; }
        public DateTime? PublishingDate{ get; set; }
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