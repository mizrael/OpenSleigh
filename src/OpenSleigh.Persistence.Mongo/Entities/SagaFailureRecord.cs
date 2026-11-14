using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace OpenSleigh.Persistence.Mongo.Entities;

/// <summary>
/// one failed attempt to handle a message, and what was decided for it.
/// </summary>
/// <remarks>
/// A collection of its own, not a field of the saga: the saga document is read for every message,
/// and failures must not make it larger.
/// </remarks>
[BsonIgnoreExtraElements]
public class SagaFailureRecord
{
    public ObjectId Id { get; init; }

    public required string FailureId { get; init; }
    public required string InstanceId { get; init; }
    public required string CorrelationId { get; init; }
    public required string SagaType { get; init; }
    public required string MessageId { get; init; }
    public required string MessageType { get; init; }
    public required int Attempt { get; init; }

    /// <summary>
    /// in UTC. A date, so that the records can be sorted, purged and expired by it through an index.
    /// </summary>
    public required DateTime OccurredAt { get; init; }

    public required string ExceptionType { get; init; }
    public required string ExceptionMessage { get; init; }

    [BsonIgnoreIfNull]
    public string? StackTrace { get; init; }

    [BsonRepresentation(BsonType.String)]
    public required FailureDecision Decision { get; init; }

    [BsonIgnoreIfNull]
    public DateTime? RetryAt { get; init; }

    [BsonIgnoreIfNull]
    public string? CompensationError { get; init; }

    public static SagaFailureRecord Create(SagaMessageFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new SagaFailureRecord
        {
            FailureId = failure.Id,
            InstanceId = failure.InstanceId,
            CorrelationId = failure.CorrelationId,
            SagaType = failure.SagaType,
            MessageId = failure.MessageId,
            MessageType = failure.MessageType,
            Attempt = failure.Attempt,
            OccurredAt = failure.OccurredAt.UtcDateTime,
            ExceptionType = failure.ExceptionType,
            ExceptionMessage = failure.ExceptionMessage,
            StackTrace = failure.StackTrace,
            Decision = failure.Decision,
            RetryAt = failure.RetryAt?.UtcDateTime,
            CompensationError = failure.CompensationError
        };
    }

    public SagaMessageFailure ToFailure()
        => new()
        {
            Id = FailureId,
            InstanceId = InstanceId,
            CorrelationId = CorrelationId,
            SagaType = SagaType,
            MessageId = MessageId,
            MessageType = MessageType,
            Attempt = Attempt,
            OccurredAt = InUtc(OccurredAt),
            ExceptionType = ExceptionType,
            ExceptionMessage = ExceptionMessage,
            StackTrace = StackTrace,
            Decision = Decision,
            RetryAt = RetryAt is null ? null : InUtc(RetryAt.Value),
            CompensationError = CompensationError
        };

    private static DateTimeOffset InUtc(DateTime date)
        => new(DateTime.SpecifyKind(date, DateTimeKind.Utc));
}
