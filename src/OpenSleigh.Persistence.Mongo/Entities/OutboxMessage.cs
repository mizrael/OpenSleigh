using MongoDB.Bson;
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
            parentId: ParentId,
            senderId: SenderId,
            serializer,
            out envelope);
    }

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
            ParentId = message.ParentId,
            SenderId = message.SenderId,
        };
    }
}