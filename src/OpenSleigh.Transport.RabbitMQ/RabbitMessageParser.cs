using Microsoft.Extensions.Logging;
using OpenSleigh.Outbox;
using OpenSleigh.Utils;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Globalization;
using System.Text;

namespace OpenSleigh.Transport.RabbitMQ;

internal sealed class RabbitMessageParser : IRabbitMessageParser
{
    private readonly ITypeResolver _typeResolver;
    private readonly ISerializer _serializer;

    public RabbitMessageParser(
        ITypeResolver typeResolver,
        ISerializer serializer)
    {
        _typeResolver = typeResolver ?? throw new ArgumentNullException(nameof(typeResolver));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    public Task<MessageEnvelope> ParseMessageAsync(BasicDeliverEventArgs eventArgs)
        => Task.FromResult(Parse(eventArgs.BasicProperties, eventArgs.Body.Span));

    public MessageEnvelope Parse(IReadOnlyBasicProperties properties, ReadOnlySpan<byte> body)
    {
        var messageId = properties.MessageId;
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId, nameof(messageId));

        var correlationId = properties.CorrelationId;
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId, nameof(correlationId));

        var messageTypeName = properties.GetHeaderValue(nameof(MessageEnvelope.MessageType));
        ArgumentException.ThrowIfNullOrWhiteSpace(messageTypeName, nameof(messageTypeName));
        var messageType = _typeResolver.Resolve(messageTypeName, throwOnError: true);

        var senderId = properties.GetHeaderValue(nameof(MessageEnvelope.SenderId));
        ArgumentException.ThrowIfNullOrWhiteSpace(senderId, nameof(senderId));

        var createdAt = DateTimeOffset.Parse(properties.GetHeaderValue(nameof(MessageEnvelope.CreatedAt)));

        if (!MessageEnvelope.TryCreate(
                body,
                messageId: messageId,
                correlationId: correlationId,
                createdAt,
                messageType!,
                senderId: senderId,
                deliverAt: null,
                firstReturnedAt: FirstReturnedAt(properties),
                _serializer,
                out var message) || message is null)
        {
            throw new ArgumentException("unable to parse outbox message.");
        }

        return message!;
    }

    // only a message that came back before has it. One that cannot be read is as good as none:
    // the message is not refused for it, and its period starts again if it comes back
    private static DateTimeOffset? FirstReturnedAt(IReadOnlyBasicProperties properties)
        => properties.Headers is { } headers
           && headers.TryGetValue(nameof(MessageEnvelope.FirstReturnedAt), out var value)
           && value is byte[] bytes
           && DateTimeOffset.TryParse(Encoding.UTF8.GetString(bytes), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var firstReturnedAt)
            ? firstReturnedAt
            : null;
}
