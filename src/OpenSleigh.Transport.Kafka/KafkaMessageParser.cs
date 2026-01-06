using Confluent.Kafka;
using OpenSleigh.Outbox;
using OpenSleigh.Utils;

namespace OpenSleigh.Transport.Kafka;

public class KafkaMessageParser : IKafkaMessageParser
{
    private readonly ITypeResolver _typeResolver;
    private readonly ISerializer _serializer;

    public KafkaMessageParser(ITypeResolver typeResolver, ISerializer serializer)
    {
        _typeResolver = typeResolver ?? throw new ArgumentNullException(nameof(typeResolver));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    public MessageEnvelope Parse(ConsumeResult<string, byte[]> consumeResult)
    {
        ArgumentNullException.ThrowIfNull(consumeResult);

        if (consumeResult.Message.Headers is null)
            throw new ArgumentException("message headers cannot be null.");

        var messageTypeName = consumeResult.Message.Headers.GetHeaderValue(nameof(MessageEnvelope.MessageType));
        ArgumentNullException.ThrowIfNullOrWhiteSpace(messageTypeName);
        var messageType = _typeResolver.Resolve(messageTypeName);
        if(messageType is null) 
            throw new ArgumentException("invalid message type");

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

        if (!MessageEnvelope.TryCreate(consumeResult.Message.Value,
                                        messageId: messageId,
                                        correlationId: correlationId,
                                        createdAt, 
                                        messageType,
                                        senderId: senderId,
                                        _serializer,
                                        out var message))
            throw new ArgumentException("unable to parse outbox message.");
        return message;
    }
}
