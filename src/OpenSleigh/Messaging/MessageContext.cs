using OpenSleigh.Outbox;

namespace OpenSleigh.Messaging
{
    internal record MessageContext<TM> : IMessageContext<TM>
        where TM : IMessage
    {
        private MessageContext(string id, string correlationId, TM message, string? parentId = null, string? senderId = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException($"'{nameof(id)}' cannot be null or whitespace.", nameof(id));
            if (string.IsNullOrWhiteSpace(correlationId))
                throw new ArgumentException($"'{nameof(correlationId)}' cannot be null or whitespace.", nameof(correlationId));

            Id = id;
            CorrelationId = correlationId;

            Message = message ?? throw new ArgumentNullException(nameof(message));

            ParentId = parentId;
            SenderId = senderId;
        }

        public TM Message { get; }
        public string Id { get; }
        public string CorrelationId { get; }
        public string? ParentId { get; }
        public string? SenderId { get; }

        public static IMessageContext<TM> Create(TM message, OutboxMessage outboxMessage)
            => new MessageContext<TM>(
                id: outboxMessage.MessageId,
                correlationId: outboxMessage.CorrelationId,
                message,
                outboxMessage.ParentId,
                outboxMessage.SenderId);

        public static IMessageContext<TM> Create(TM message, ISagaExecutionContext executionContext)
            => new MessageContext<TM>(
                id: Guid.NewGuid().ToString(),
                correlationId: executionContext.CorrelationId,
                message,
                executionContext.TriggerMessageId,
                executionContext.InstanceId);        
    }
}