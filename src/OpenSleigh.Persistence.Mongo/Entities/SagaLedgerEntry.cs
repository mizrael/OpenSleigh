using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace OpenSleigh.Persistence.Mongo.Entities;

/// <summary>
/// SPIKE, not for merging: one document per append to a saga instance. It holds the state after the append,
/// the messages that were handled, and the messages the saga published, until the relay has sent them:
/// one insert stores all of it or nothing.
/// </summary>
[BsonIgnoreExtraElements]
public class SagaLedgerEntry
{
    public ObjectId Id { get; init; }

    public required string InstanceId { get; init; }
    public required int Version { get; init; }
    public required string CorrelationId { get; init; }
    public required string SagaType { get; init; }
    public string? SagaStateType { get; init; }
    public required string TriggerMessageId { get; init; }

    /// <summary>true on the first document of a saga, missing on the others.</summary>
    [BsonIgnoreIfNull]
    public bool? IsStart { get; init; }

    public string[] MessageIds { get; init; } = [];
    public DateTime OccurredAt { get; init; }
    public bool IsCompleted { get; init; }
    public byte[]? StateData { get; init; }

    public string? LeaseId { get; set; }
    public DateTime? LeaseTime { get; set; }

    [BsonIgnoreIfNull]
    public List<OutboxMessage>? Outbox { get; init; }

    /// <summary>true while the relay has messages of this document to send, missing otherwise.</summary>
    [BsonIgnoreIfNull]
    public bool? Pending { get; init; }

    public string? ClaimId { get; set; }
    public DateTime? ClaimTime { get; set; }
}
