using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using OpenSleigh.Utils;
using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Persistence.Mongo.Entities;

public record OutboxMessage
{
    public ObjectId Id { get; init; }

    public string? LockId { get; set; }
    public DateTimeOffset? LockTime { get; set; }
    
    public required string CorrelationId { get; set; }
    public required byte[] Body { get; set; }
    public required string MessageId { get; set; }
    public required string MessageType { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string? ParentId { get; set; }
    public required string SenderId { get; set; }

    /// <summary>
    /// the earliest time, in UTC, at which the relay publishes the message. Null when there is no delay.
    /// </summary>
    /// <remarks>
    /// A date, so that the relay can look the due messages up by range through an index.
    /// Not written when null: a message without a delay is stored as it was before there were delays.
    /// </remarks>
    [BsonIgnoreIfNull]
    public DateTime? DeliverAt { get; set; }

    public bool TryMap(
        ITypeResolver typeResolver,
        ISerializer serializer,
        [NotNullWhen(true)] out Outbox.MessageEnvelope? envelope)
    {
        ArgumentNullException.ThrowIfNull(typeResolver, nameof(typeResolver));

        var type = typeResolver.Resolve(MessageType, false);
        if (type is null)
        {
            envelope = null;
            return false;
        }

        return Outbox.MessageEnvelope.TryCreate(
            Body,
            messageId: MessageId,
            correlationId: CorrelationId,
            CreatedAt,
            type,
            senderId: SenderId,
            deliverAt: DueTime(),
            serializer,
            out envelope);
    }

    private DateTimeOffset? DueTime()
        => DeliverAt is null
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(DeliverAt.Value, DateTimeKind.Utc));

    public static OutboxMessage Create(
        Outbox.MessageEnvelope message,
        ISerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(message, nameof(message));

        var body = serializer.Serialize(message.Message);

        return new OutboxMessage()
        {
            Body = body,
            MessageId = message.MessageId,
            CorrelationId = message.CorrelationId,
            CreatedAt = message.CreatedAt,
            MessageType = message.MessageType.FullName!,
            SenderId = message.SenderId,
            DeliverAt = message.DeliverAt?.UtcDateTime,
        };
    }
}