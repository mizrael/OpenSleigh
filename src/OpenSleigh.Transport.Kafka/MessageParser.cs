using Confluent.Kafka;
using OpenSleigh.Outbox;
using OpenSleigh.Utils;

namespace OpenSleigh.Transport.Kafka
{
    public class MessageParser : IMessageParser
    {
        private readonly ISerializer _serializer;
        private readonly IQueueReferenceFactory _queueReferenceFactory;

        public MessageParser(ISerializer serializer, IQueueReferenceFactory queueReferenceFactory)
        {     
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _queueReferenceFactory = queueReferenceFactory ?? throw new ArgumentNullException(nameof(queueReferenceFactory));
        }

        public OutboxMessage Parse(ConsumeResult<string, ReadOnlyMemory<byte>> consumeResult)
        {
            if (consumeResult is null)
                throw new ArgumentNullException(nameof(consumeResult));
            
            var messageType = _queueReferenceFactory.GetQueueType(consumeResult.Topic);
            if(messageType is null) 
                throw new ArgumentException("invalid message type");

            var messageId = consumeResult.Message.Headers.GetHeaderValue(nameof(OutboxMessage.MessageId));
            if (string.IsNullOrWhiteSpace(messageId))
                throw new ArgumentException("message id cannot be null.");

            var senderId = consumeResult.Message.Headers.GetHeaderValue(nameof(OutboxMessage.SenderId));
            if (string.IsNullOrWhiteSpace(senderId))
                throw new ArgumentException("sender id cannot be null.");

            var correlationId = consumeResult.Message.Headers.GetHeaderValue(nameof(OutboxMessage.CorrelationId));
            if (string.IsNullOrWhiteSpace(correlationId))
                throw new ArgumentException("correlation id cannot be null.");

            var parentId = consumeResult.Message.Headers.GetHeaderValue(nameof(OutboxMessage.ParentId));
            if (string.IsNullOrWhiteSpace(correlationId))
                throw new ArgumentException("parent id cannot be null.");

            var createdAt = DateTimeOffset.Parse(consumeResult.Message.Headers.GetHeaderValue(nameof(OutboxMessage.CreatedAt)));

            if (!OutboxMessage.TryCreate(consumeResult.Message.Value,
                                            messageId: messageId,
                                            correlationId: correlationId,
                                            createdAt, messageType,
                                            parentId: parentId,
                                            senderId: senderId,
                                            out var message))
                throw new ArgumentException("unable to parse outbox message.");
            return message;
        }
    }
}
