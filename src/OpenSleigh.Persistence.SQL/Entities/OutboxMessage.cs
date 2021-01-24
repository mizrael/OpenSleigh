using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OpenSleigh.Persistence.SQL.Entities
{
    public record OutboxMessage(Guid Id, byte[] Data, string Type, string Status,
        DateTime? PublishingDate = null,
        Guid? LockId = null, DateTime? LockTime = null);
    
    internal class OutboxMessageStateEntityTypeConfiguration : IEntityTypeConfiguration<OutboxMessage>
    {
        public void Configure(EntityTypeBuilder<OutboxMessage> builder)
        {
            builder.ToTable("OutboxMessages", "dbo");

            builder.HasKey(e => e.Id);
        }
    }
}