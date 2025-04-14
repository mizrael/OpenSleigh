using Confluent.Kafka;
using OpenSleigh.Outbox;

namespace OpenSleigh.Transport.Kafka;

public class MessageParser : IMessageParser
{
    private readonly IQueueReferenceFactory _queueReferenceFactory;

    public MessageParser(IQueueReferenceFactory queueReferenceFactory)
    {     
        _queueReferenceFactory = queueReferenceFactory ?? throw new ArgumentNullException(nameof(queueReferenceFactory));
    }

    public OutboxMessage Parse(ConsumeResult<string, byte[]> consumeResult)
    {
        if (consumeResult is null)
            throw new ArgumentNullException(nameof(consumeResult));
        
        var messageType = _queueReferenceFactory.GetQueueType(consumeResult.Topic);
        if(messageType is null) 
            throw new ArgumentException("invalid message type");

        if(consumeResult.Message.Headers is null)
            throw new ArgumentException("message headers cannot be null.");

        var messageId = consumeResult.Message.Key;
        if (string.IsNullOrWhiteSpace(messageId))
            throw new ArgumentException("message id cannot be null.");

        var senderId = consumeResult.Message.Headers.GetHeaderValue(nameof(OutboxMessage.SenderId));
        if (string.IsNullOrWhiteSpace(senderId))
            throw new ArgumentException("sender id cannot be null.");

        var correlationId = consumeResult.Message.Headers.GetHeaderValue(nameof(OutboxMessage.CorrelationId));
        if (string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("correlation id cannot be null.");

        var createdAt = DateTimeOffset.Parse(consumeResult.Message.Headers.GetHeaderValue(nameof(OutboxMessage.CreatedAt)));

        consumeResult.Message.Headers.TryGetHeaderValue(nameof(OutboxMessage.ParentId), out var parentId);

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
