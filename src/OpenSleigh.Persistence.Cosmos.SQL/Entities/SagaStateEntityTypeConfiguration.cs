using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OpenSleigh.Persistence.Cosmos.SQL.Entities
{
    [ExcludeFromCodeCoverage]
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
