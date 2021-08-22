using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OpenSleigh.Persistence.SQL.Entities
{
    public record OutboxMessage(Guid Id, byte[] Data, string Type)
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
            builder.ToTable("OutboxMessages", "dbo");

            builder.HasKey(e => e.Id);
        }
    }
}