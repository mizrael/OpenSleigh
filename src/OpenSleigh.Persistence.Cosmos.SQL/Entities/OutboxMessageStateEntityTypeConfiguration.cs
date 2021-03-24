using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OpenSleigh.Persistence.Cosmos.SQL.Entities
{
    [ExcludeFromCodeCoverage]
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