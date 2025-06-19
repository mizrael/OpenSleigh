using OpenSleigh.Outbox;

namespace OpenSleigh.Transport;

public interface IMessageContext<out TM> where TM : IMessage
{
    TM Message { get; }
    string MessageId { get; }
    string CorrelationId { get; }
    string SenderId { get; }
}

internal record DefaultMessageContext<TM> : IMessageContext<TM>
    where TM : IMessage
{
    private DefaultMessageContext(string messageId, string correlationId, TM message, string? senderId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId, nameof(messageId));
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId, nameof(correlationId));
        ArgumentException.ThrowIfNullOrWhiteSpace(senderId, nameof(senderId));
        
        Message = message ?? throw new ArgumentNullException(nameof(message));

        MessageId = messageId;
        CorrelationId = correlationId;
        SenderId = senderId;
    }

    public TM Message { get; }
    public string MessageId { get; }
    public string CorrelationId { get; }
    public string SenderId { get; }

    public static IMessageContext<TM> Create(MessageEnvelope outboxMessage)
        => new DefaultMessageContext<TM>(
            messageId: outboxMessage.MessageId,
            correlationId: outboxMessage.CorrelationId,
            (TM)outboxMessage.Message,
            outboxMessage.SenderId);
}