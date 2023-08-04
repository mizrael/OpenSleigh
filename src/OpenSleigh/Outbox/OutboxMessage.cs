using OpenSleigh.Transport;
using OpenSleigh.Utils;

namespace OpenSleigh.Outbox
{
    public class OutboxMessage
    { 
        private OutboxMessage() { }

        public IMessage GetMessage(ISerializer serializer)
        {
            if (serializer is null)
                throw new ArgumentNullException(nameof(serializer));
            
            var instance = serializer.Deserialize(this.Body.Span, this.MessageType);
            if (instance is null || instance is not IMessage message)
                throw new DataMisalignedException($"Unable to deserialize message '{this.MessageId}' to type '{this.MessageType}'.");
            
            return message;
        }

        public static bool TryCreate(
            ReadOnlyMemory<byte> body,
            string messageId,
            string correlationId,
            DateTimeOffset createdAt,
            Type messageType,
            string? parentId,
            string senderId,
            out OutboxMessage? message)
        {
            if (body.Length == 0 ||
                string.IsNullOrEmpty(messageId) ||
                string.IsNullOrEmpty(correlationId) ||
                createdAt == default ||
                messageType is null || 
                string.IsNullOrEmpty(senderId))
            {
                message = null;
                return false;
            }

            message = new OutboxMessage()
            {
                Body = body,
                MessageId = messageId,
                CorrelationId = correlationId,
                CreatedAt = createdAt,
                MessageType = messageType,
                ParentId = parentId,
                SenderId = senderId
            };
            return true;
        }
    
        public static OutboxMessage Create(
           IMessage message,
           ISystemInfo systemInfo,
           ISerializer serializer)
        {
            if (message is null)
                throw new ArgumentNullException(nameof(message));

            if (serializer is null)
                throw new ArgumentNullException(nameof(serializer));

            return new OutboxMessage()
            {
                CorrelationId = Guid.NewGuid().ToString(),
                SenderId = systemInfo.Id,
                MessageId = Guid.NewGuid().ToString(),
                Body = serializer.Serialize(message),
                MessageType = message.GetType(),
                CreatedAt = DateTimeOffset.UtcNow
            };
        }

        public static OutboxMessage Create(
            IMessage message,
            ISerializer serializer,
            ISagaExecutionContext executionContext)
        {
            if (message is null)
                throw new ArgumentNullException(nameof(message));

            if (serializer is null)
                throw new ArgumentNullException(nameof(serializer));
            
            if (executionContext is null)            
                throw new ArgumentNullException(nameof(executionContext));            

            return new OutboxMessage()
            {
                CorrelationId = executionContext.CorrelationId,
                MessageId = Guid.NewGuid().ToString(),
                Body = serializer.Serialize(message),
                MessageType = message.GetType(),
                CreatedAt = DateTimeOffset.UtcNow,
                ParentId = executionContext.TriggerMessageId,
                SenderId = executionContext.InstanceId
            };
        }

        public required string CorrelationId { get; init; }
        public required ReadOnlyMemory<byte> Body { get; init; }
        public required string MessageId { get; init; }
        public required Type MessageType { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
        public required string SenderId { get; init; }        
        public string? ParentId { get; init; }        
    }
}