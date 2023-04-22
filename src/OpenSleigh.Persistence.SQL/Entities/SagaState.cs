using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OpenSleigh.Persistence.SQL.Entities
{
    public class SagaState
    {
        public required string CorrelationId { get; init; }
        public required string InstanceId { get; init; }
        public required string TriggerMessageId { get; init; }
        public required string SagaType { get; init; }
        public required string? SagaStateType { get; init; }
        public byte[]? StateData { get; set; }
        public bool IsCompleted { get; set; } 

        public ICollection<SagaProcessedMessage> ProcessedMessages { get; init; } = new HashSet<SagaProcessedMessage>();

        public string? LockId { get; set; }
        public DateTimeOffset? LockTime { get; set; }
    }

    internal class SagaStateEntityTypeConfiguration : IEntityTypeConfiguration<SagaState>
    {
        public void Configure(EntityTypeBuilder<SagaState> builder)
        {
            builder.ToTable("SagaStates", Constants.DbSchema);
            
            builder.HasKey(e => e.InstanceId);
            builder.HasIndex(e => new { e.CorrelationId, e.SagaType, e.SagaStateType });

            builder.HasMany(e => e.ProcessedMessages)
                .WithOne(e => e.SagaState);
        }
    }
}
