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

    /// <summary>
    /// the earliest time, in UTC, at which the outbox relay publishes the message.
    /// Null when the message is published as soon as the relay reads it.
    /// </summary>
    public DateTimeOffset? DeliverAt { get; init; }

    /// <summary>
    /// when, in UTC, the transport first gave the message back because nothing could take it.
    /// Null for a message that never came back. It is stored with the message and published with it,
    /// so that whoever gets it back again knows for how long it has been coming back.
    /// </summary>
    public DateTimeOffset? FirstReturnedAt { get; init; }

    /// <summary>
    /// the same message, to be put back in the outbox after the transport gave it back.
    /// </summary>
    /// <param name="firstReturnedAt">when it was first given back, which is not earlier than its creation.</param>
    /// <param name="deliverAt">the earliest time at which the relay publishes it again.</param>
    public MessageEnvelope Returned(DateTimeOffset firstReturnedAt, DateTimeOffset deliverAt)
        => new()
        {
            Message = Message,
            MessageId = MessageId,
            CorrelationId = CorrelationId,
            CreatedAt = CreatedAt,
            SenderId = SenderId,
            DeliverAt = deliverAt.ToUniversalTime(),
            FirstReturnedAt = firstReturnedAt.ToUniversalTime(),
        };

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
        => TryCreate(body, messageId, correlationId, createdAt, messageType, senderId, deliverAt: null, serializer, out result);

    /// <param name="deliverAt">the earliest time at which the relay publishes the message, null for no delay.</param>
    public static bool TryCreate(
        ReadOnlySpan<byte> body,
        string messageId,
        string correlationId,
        DateTimeOffset createdAt,
        Type messageType,
        string senderId,
        DateTimeOffset? deliverAt,
        ISerializer serializer,
        [NotNullWhen(true)] out MessageEnvelope? result)
        => TryCreate(body, messageId, correlationId, createdAt, messageType, senderId, deliverAt, firstReturnedAt: null, serializer, out result);

    /// <param name="deliverAt">the earliest time at which the relay publishes the message, null for no delay.</param>
    /// <param name="firstReturnedAt">when the transport first gave the message back, null when it never did.</param>
    public static bool TryCreate(
        ReadOnlySpan<byte> body,
        string messageId,
        string correlationId,
        DateTimeOffset createdAt,
        Type messageType,
        string senderId,
        DateTimeOffset? deliverAt,
        DateTimeOffset? firstReturnedAt,
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

        // the type is named by whoever sent the message: nothing else is built from the body
        if (!typeof(IMessage).IsAssignableFrom(messageType))
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
            CreatedAt = createdAt.ToUniversalTime(),
            SenderId = senderId,
            DeliverAt = deliverAt?.ToUniversalTime(),
            FirstReturnedAt = firstReturnedAt?.ToUniversalTime()
        };
        return true;
    }

    public static MessageEnvelope Create(
       IMessage message,
       ISystemInfo systemInfo)
        => Create(message, systemInfo, TimeSpan.Zero);

    /// <param name="delay">how long the relay waits before it publishes the message. Zero for no delay.</param>
    public static MessageEnvelope Create(
       IMessage message,
       ISystemInfo systemInfo,
       TimeSpan delay)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);

        var correlationId = message is IHasCorrelationId cm
            ? cm.CorrelationId
#if NET9_0_OR_GREATER
            : Guid.CreateVersion7().ToString("N");
#else
            : Guid.NewGuid().ToString("N");
#endif

        var messageId = message is IIdempotentMessage im
            ? im.GetIdempotencyKey()
#if NET9_0_OR_GREATER
            : Guid.CreateVersion7().ToString("N");
#else
            : Guid.NewGuid().ToString("N");
#endif

        var createdAt = DateTimeOffset.UtcNow;

        return new MessageEnvelope()
        {
            CorrelationId = correlationId,
            SenderId = systemInfo.Id,
            MessageId = messageId,
            Message = message,
            CreatedAt = createdAt,
            DeliverAt = DueTime(createdAt, delay)
        };
    }

    public static MessageEnvelope Create(
        IMessage message,
        ISagaInstance sagaInstance)
        => Create(message, sagaInstance, TimeSpan.Zero);

    /// <param name="delay">how long the relay waits before it publishes the message. Zero for no delay.</param>
    public static MessageEnvelope Create(
        IMessage message,
        ISagaInstance sagaInstance,
        TimeSpan delay)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(sagaInstance);
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);

        var correlationId = message is IHasCorrelationId cm ?
            cm.CorrelationId : sagaInstance.CorrelationId;

        var messageId = message is IIdempotentMessage im
            ? im.GetIdempotencyKey()
#if NET9_0_OR_GREATER
            : Guid.CreateVersion7().ToString("N");
#else
            : Guid.NewGuid().ToString("N");
#endif

        var createdAt = DateTimeOffset.UtcNow;

        return new MessageEnvelope()
        {
            Message = message,
            CreatedAt = createdAt,
            MessageId = messageId,
            CorrelationId = sagaInstance.CorrelationId,
            SenderId = sagaInstance.InstanceId,
            DeliverAt = DueTime(createdAt, delay)
        };
    }

    private static DateTimeOffset? DueTime(DateTimeOffset createdAt, TimeSpan delay)
        => delay == TimeSpan.Zero ? null : createdAt + delay;

    #endregion Factory
}