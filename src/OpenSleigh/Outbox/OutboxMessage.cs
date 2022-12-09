﻿using OpenSleigh.Messaging;
using OpenSleigh.Utils;

namespace OpenSleigh.Outbox
{
    public class OutboxMessage
    {
        public IMessage GetMessage(ISerializer serializer)
        {
            if (serializer is null)
                throw new ArgumentNullException(nameof(serializer));

            var instance = serializer.Deserialize(Body.Span, MessageType);
            if (instance is null || instance is not IMessage message)
                throw new DataMisalignedException($"Unable to deserialize message '{this.MessageId}' to type '{this.MessageType.FullName}'.");
            
            return message;
        }

        public static OutboxMessage Create(
           IMessage message,
           ISerializer serializer)
        {
            if (message is null)
                throw new ArgumentNullException(nameof(message));

            if (serializer is null)
                throw new ArgumentNullException(nameof(serializer));

            return new OutboxMessage()
            {
                CorrelationId = Guid.NewGuid().ToString(),
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

        public required string CorrelationId { get; set; }
        public required ReadOnlyMemory<byte> Body { get; set; }
        public required string MessageId { get; set; }
        public required Type MessageType { get; set; }
        public required DateTimeOffset CreatedAt { get; set; }
        public string? ParentId { get; set; }
        public string? SenderId { get; set; }
    }
}