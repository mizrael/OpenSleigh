using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OpenSleigh.Persistence.SQL.Entities
{
    public class SagaState
    {
        public required string CorrelationId { get; init; }
        public required string InstanceId { get; init; }
        public required string TriggerMessageId { get; init; }
        public required string SagaType { get; set; }
        public required string? SagaStateType { get; set; }
        public byte[]? StateData { get; set; }
        public bool IsCompleted { get; init; } //TODO: this is not persisted/mapped

        public ICollection<string> ProcessedMessages { get; init; } = new HashSet<string>();

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
        }
    }

}
