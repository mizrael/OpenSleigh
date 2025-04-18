using Confluent.Kafka;
using OpenSleigh.Outbox;
using OpenSleigh.Utils;

namespace OpenSleigh.Transport.Kafka;

public class MessageParser : IMessageParser
{
    private readonly IQueueReferenceFactory _queueReferenceFactory;
    private readonly ISerializer _serializer;

    public MessageParser(IQueueReferenceFactory queueReferenceFactory, ISerializer serializer)
    {
        _queueReferenceFactory = queueReferenceFactory ?? throw new ArgumentNullException(nameof(queueReferenceFactory));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    public MessageEnvelope Parse(ConsumeResult<string, byte[]> consumeResult)
    {
        ArgumentNullException.ThrowIfNull(consumeResult);

        var messageType = _queueReferenceFactory.GetQueueType(consumeResult.Topic);
        if(messageType is null) 
            throw new ArgumentException("invalid message type");

        if(consumeResult.Message.Headers is null)
            throw new ArgumentException("message headers cannot be null.");

        var messageId = consumeResult.Message.Key;
        if (string.IsNullOrWhiteSpace(messageId))
            throw new ArgumentException("message id cannot be null.");

        var senderId = consumeResult.Message.Headers.GetHeaderValue(nameof(MessageEnvelope.SenderId));
        if (string.IsNullOrWhiteSpace(senderId))
            throw new ArgumentException("sender id cannot be null.");

        var correlationId = consumeResult.Message.Headers.GetHeaderValue(nameof(MessageEnvelope.CorrelationId));
        if (string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("correlation id cannot be null.");

        var createdAt = DateTimeOffset.Parse(consumeResult.Message.Headers.GetHeaderValue(nameof(MessageEnvelope.CreatedAt)));

        consumeResult.Message.Headers.TryGetHeaderValue(nameof(MessageEnvelope.ParentId), out var parentId);

        if (!MessageEnvelope.TryCreate(consumeResult.Message.Value,
                                        messageId: messageId,
                                        correlationId: correlationId,
                                        createdAt, 
                                        messageType,
                                        parentId: parentId,
                                        senderId: senderId,
                                        _serializer,
                                        out var message))
            throw new ArgumentException("unable to parse outbox message.");
        return message;
    }
}
