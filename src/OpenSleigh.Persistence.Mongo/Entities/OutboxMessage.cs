using MongoDB.Bson;
using OpenSleigh.Utils;

namespace OpenSleigh.Persistence.Mongo.Entities
{
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

        public Outbox.OutboxMessage? ToModel(ITypeResolver typeResolver)
        {
            Outbox.OutboxMessage.TryCreate(
                Body,
                messageId: MessageId,
                correlationId: CorrelationId,
                CreatedAt,
                typeResolver.Resolve(MessageType, false),
                parentId: ParentId,
                senderId: SenderId,
                out var result);
            return result;
        }

        public static OutboxMessage Create(Outbox.OutboxMessage message)
        {
            return new OutboxMessage()
            {
                Body = message.Body.ToArray(),
                MessageId = message.MessageId,
                CorrelationId = message.CorrelationId,
                CreatedAt = message.CreatedAt,
                MessageType = message.MessageType.FullName,
                ParentId = message.ParentId,
                SenderId = message.SenderId,
            };
        }
    }
}