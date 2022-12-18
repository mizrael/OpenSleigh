using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace OpenSleigh.Persistence.SQL.Entities
{
    public class SagaProcessedMessage
    {
        public required string InstanceId { get; init; }
        public required string MessageId { get; init; }
        public required DateTimeOffset When { get; init; }

        public SagaState SagaState { get; init; }
    }

    internal class SagaProcessedMessageTypeConfiguration : IEntityTypeConfiguration<SagaProcessedMessage>
    {
        public void Configure(EntityTypeBuilder<SagaProcessedMessage> builder)
        {
            builder.ToTable("SagaProcessedMessages", Constants.DbSchema);

            builder.HasKey(e => new { e.InstanceId, e.MessageId });
        }
    }
}
