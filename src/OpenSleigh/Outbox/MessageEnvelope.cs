using OpenSleigh.Transport;
using OpenSleigh.Utils;
using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Outbox;

public class MessageEnvelope
{ 
    private MessageEnvelope() { }

    public static bool TryCreate(
        ReadOnlySpan<byte> body,
        string messageId,
        string correlationId,
        DateTimeOffset createdAt,
        Type messageType,
        string? parentId,
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
        if(message is null)
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
            MessageType = messageType,
            ParentId = parentId,
            SenderId = senderId
        };
        return true;
    }

    public static MessageEnvelope Create(
       IMessage message,
       ISystemInfo systemInfo)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new MessageEnvelope()
        {
            CorrelationId = Guid.CreateVersion7().ToString(),
            SenderId = systemInfo.Id,
            MessageId = Guid.CreateVersion7().ToString(),
            Message = message,
            MessageType = message.GetType(),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public static MessageEnvelope Create(
        IMessage message,
        ISagaExecutionContext executionContext)
    {
        ArgumentNullException.ThrowIfNull(message);

        ArgumentNullException.ThrowIfNull(executionContext);

        return new MessageEnvelope()
        {
            Message = message,
            MessageType = message.GetType(),
            CreatedAt = DateTimeOffset.UtcNow,
            MessageId = Guid.CreateVersion7().ToString(),
            CorrelationId = executionContext.CorrelationId,
            ParentId = executionContext.TriggerMessageId,
            SenderId = executionContext.InstanceId
        };
    }
    
    public required IMessage Message { get; init; }

    // TODO: we don't need this anymore
    public required Type MessageType { get; init; }
    
    public required string CorrelationId { get; init; }
    public required string MessageId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string SenderId { get; init; }        
    public string? ParentId { get; init; }        
}