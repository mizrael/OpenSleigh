using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OpenSleigh.Persistence.Cosmos.SQL.Entities
{
    public record SagaState(string PartitionKey, Guid CorrelationId, string Type)
    {
        public byte[] Data { get; set; } = null;
        public Guid? LockId { get; set; } = null;
        public DateTime? LockTime { get; set; } = null;
    }

    internal class SagaStateEntityTypeConfiguration : IEntityTypeConfiguration<SagaState>
    {
        public void Configure(EntityTypeBuilder<SagaState> builder)
        {
            builder.ToContainer("SagaStates")
                            .HasNoDiscriminator();

            builder.HasKey(e => new {e.CorrelationId, e.Type});
            builder.HasPartitionKey(e => e.PartitionKey);
        }
    }

}
