using OpenSleigh.Transport;
using OpenSleigh.Utils;
using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Outbox;

public class MessageEnvelope
{   
    private MessageEnvelope() { }

    private IMessage _message;
    public required IMessage Message 
    {
        get => _message;
        init 
        { 
            ArgumentNullException.ThrowIfNull(value, nameof(value));
            _message = value;
            _messageType = value.GetType();
        }
    }

    private Type _messageType;
    public Type MessageType => _messageType;

    public required string CorrelationId { get; init; }
    public required string MessageId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string SenderId { get; init; }        

    #region Factory

    public static bool TryCreate(
        ReadOnlySpan<byte> body,
        string messageId,
        string correlationId,
        DateTimeOffset createdAt,
        Type messageType,
        string senderId,
        ISerializer serializer,
        [NotNullWhen(true)] out MessageEnvelope? result)
    {
        ArgumentNullException.ThrowIfNull(serializer, nameof(serializer));

        if (body.Length == 0 ||
            string.IsNullOrEmpty(messageId) ||
            string.IsNullOrEmpty(correlationId) ||
            createdAt == default ||
            messageType is null ||
            string.IsNullOrEmpty(senderId))
        {
            result = null;
            return false;
        }

        var message = serializer.Deserialize(body, messageType) as IMessage;
        if (message is null)
        {
            result = null;
            return false;
        }

        result = new MessageEnvelope()
        {
            Message = message,
            MessageId = messageId,
            CorrelationId = correlationId,
            CreatedAt = createdAt,
            SenderId = senderId
        };
        return true;
    }

    public static MessageEnvelope Create(
       IMessage message,
       ISystemInfo systemInfo)
    {
        ArgumentNullException.ThrowIfNull(message);

        var correlationId = message is IHasCorrelationId cm ?
            cm.CorrelationId : Guid.CreateVersion7().ToString("N");

        var messageId = message is IIdempotentMessage im ?
            im.GetIdempotencyKey() : Guid.CreateVersion7().ToString("N");

        return new MessageEnvelope()
        {
            CorrelationId = correlationId,
            SenderId = systemInfo.Id,
            MessageId = messageId,
            Message = message,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public static MessageEnvelope Create(
        IMessage message,
        ISagaInstance sagaInstance)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(sagaInstance);

        var correlationId = message is IHasCorrelationId cm ?
            cm.CorrelationId : sagaInstance.CorrelationId;

        var messageId = message is IIdempotentMessage im ?
            im.GetIdempotencyKey() : Guid.CreateVersion7().ToString("N");

        return new MessageEnvelope()
        {
            Message = message,
            CreatedAt = DateTimeOffset.UtcNow,
            MessageId = messageId,
            CorrelationId = sagaInstance.CorrelationId,
            SenderId = sagaInstance.InstanceId
        };
    }

    #endregion Factory
}