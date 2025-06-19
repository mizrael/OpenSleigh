using OpenSleigh.Transport;
using OpenSleigh.Utils;
using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Outbox;

public class MessageEnvelope
{
    private Type _messageType;

    private MessageEnvelope() { }

    public required IMessage Message { get; init; }

    public Type MessageType
    {
        get
        {
            if(this.Message is null)
                throw new InvalidOperationException("Message is null. Cannot determine message type.");

            _messageType ??= this.Message.GetType();

            return _messageType;
        }
    }

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

        var messageId = $"{message.GetType().Name.ToLower()}-{correlationId}";

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
        ISagaInstance  executionContext)
    {
        ArgumentNullException.ThrowIfNull(message);

        ArgumentNullException.ThrowIfNull(executionContext);

        var messageId = $"{message.GetType().Name.ToLower()}-{executionContext.CorrelationId}";

        return new MessageEnvelope()
        {
            Message = message,
            CreatedAt = DateTimeOffset.UtcNow,
            MessageId = messageId,
            CorrelationId = executionContext.CorrelationId,
            SenderId = executionContext.InstanceId
        };
    }

    #endregion Factory
}