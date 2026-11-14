using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OpenSleigh.Persistence.SQL.Entities;

/// <summary>
/// one failed attempt to handle a message, and what was decided for it.
/// </summary>
/// <remarks>
/// A table of its own, not columns of the saga: the saga row is read for every message, and failures must not make it larger.
/// It does not reference the saga, so that a failure can be stored whatever happens to the saga.
/// </remarks>
public class SagaFailureRecord
{
    public const string TableName = "SagaMessageFailures";
    public const string MessageIndexName = "IX_SagaMessageFailures_InstanceId_MessageId";
    public const string CorrelationIndexName = "IX_SagaMessageFailures_CorrelationId";
    public const string OccurredAtIndexName = "IX_SagaMessageFailures_OccurredAt";

    public required string Id { get; init; }
    public required string InstanceId { get; init; }
    public required string CorrelationId { get; init; }
    public required string SagaType { get; init; }
    public required string MessageId { get; init; }
    public required string MessageType { get; init; }
    public required int Attempt { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public required string ExceptionType { get; init; }
    public required string ExceptionMessage { get; init; }
    public string? StackTrace { get; init; }
    public required FailureDecision Decision { get; init; }
    public DateTimeOffset? RetryAt { get; init; }
    public string? CompensationError { get; init; }

    public static SagaFailureRecord Map(SagaMessageFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new SagaFailureRecord
        {
            Id = failure.Id,
            InstanceId = failure.InstanceId,
            CorrelationId = failure.CorrelationId,
            SagaType = failure.SagaType,
            MessageId = failure.MessageId,
            MessageType = failure.MessageType,
            Attempt = failure.Attempt,
            OccurredAt = failure.OccurredAt.ToUniversalTime(),
            ExceptionType = failure.ExceptionType,
            ExceptionMessage = failure.ExceptionMessage,
            StackTrace = failure.StackTrace,
            Decision = failure.Decision,
            RetryAt = failure.RetryAt?.ToUniversalTime(),
            CompensationError = failure.CompensationError
        };
    }

    public SagaMessageFailure ToFailure()
        => new()
        {
            Id = Id,
            InstanceId = InstanceId,
            CorrelationId = CorrelationId,
            SagaType = SagaType,
            MessageId = MessageId,
            MessageType = MessageType,
            Attempt = Attempt,
            OccurredAt = OccurredAt,
            ExceptionType = ExceptionType,
            ExceptionMessage = ExceptionMessage,
            StackTrace = StackTrace,
            Decision = Decision,
            RetryAt = RetryAt,
            CompensationError = CompensationError
        };
}

internal class SagaFailureRecordTypeConfiguration : IEntityTypeConfiguration<SagaFailureRecord>
{
    public void Configure(EntityTypeBuilder<SagaFailureRecord> builder)
    {
        builder.ToTable(SagaFailureRecord.TableName, Constants.DbSchema);

        builder.HasKey(e => e.Id);

        // stored by name: the table is also read by people
        builder.Property(e => e.Decision).HasConversion<string>();

        builder.HasIndex(e => new { e.InstanceId, e.MessageId })
            .HasDatabaseName(SagaFailureRecord.MessageIndexName);

        builder.HasIndex(e => e.CorrelationId)
            .HasDatabaseName(SagaFailureRecord.CorrelationIndexName);

        builder.HasIndex(e => e.OccurredAt)
            .HasDatabaseName(SagaFailureRecord.OccurredAtIndexName);
    }
}
