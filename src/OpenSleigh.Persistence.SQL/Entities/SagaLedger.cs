using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OpenSleigh.Persistence.SQL.Entities;

// SPIKE, not for merging: the head and the entries of the ledger experiment.

/// <summary>
/// one row per saga instance: its current state and the version it is at.
/// </summary>
public class SagaHead
{
    public const string TableName = "Sagas";

    public required string InstanceId { get; init; }
    public required string CorrelationId { get; init; }
    public required string SagaType { get; init; }
    public string? SagaStateType { get; init; }
    public required string TriggerMessageId { get; init; }
    public int Version { get; set; }
    public bool IsCompleted { get; set; }
    public byte[]? StateData { get; set; }
    public string? LeaseId { get; set; }
    public DateTimeOffset? LeaseTime { get; set; }
}

/// <summary>
/// one row per message a saga instance handled.
/// </summary>
public class SagaLedgerEntry
{
    public const string TableName = "SagaLedger";

    public required string InstanceId { get; init; }
    public required int Version { get; init; }
    public required string MessageId { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public byte[]? StateData { get; init; }
}

internal class SagaHeadTypeConfiguration : IEntityTypeConfiguration<SagaHead>
{
    public void Configure(EntityTypeBuilder<SagaHead> builder)
    {
        builder.ToTable(SagaHead.TableName, Constants.DbSchema);

        builder.HasKey(e => e.InstanceId);
        builder.HasIndex(e => new { e.CorrelationId, e.SagaType }).IsUnique();
    }
}

internal class SagaLedgerEntryTypeConfiguration : IEntityTypeConfiguration<SagaLedgerEntry>
{
    public void Configure(EntityTypeBuilder<SagaLedgerEntry> builder)
    {
        builder.ToTable(SagaLedgerEntry.TableName, Constants.DbSchema);

        builder.HasKey(e => new { e.InstanceId, e.Version });

        // idempotency: a message is handled once by an instance
        builder.HasIndex(e => new { e.InstanceId, e.MessageId }).IsUnique();
    }
}
