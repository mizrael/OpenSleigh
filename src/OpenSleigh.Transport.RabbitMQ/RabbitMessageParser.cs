using Microsoft.Extensions.Logging;
using OpenSleigh.Outbox;
using OpenSleigh.Utils;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OpenSleigh.Transport.RabbitMQ;

internal sealed class RabbitMessageParser : IRabbitMessageParser
{
    private readonly ITypeResolver _typeResolver;
    private readonly ISerializer _serializer;
    private readonly ILogger<RabbitMessageParser> _logger;

    public RabbitMessageParser(
        ITypeResolver typeResolver,
        ISerializer serializer,
        ILogger<RabbitMessageParser> logger)
    {
        _typeResolver = typeResolver ?? throw new ArgumentNullException(nameof(typeResolver));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MessageEnvelope?> ParseMessageAsync(BasicDeliverEventArgs eventArgs, IChannel channel)
    {
        MessageEnvelope? message = null;
        try
        {
            var messageId = eventArgs.BasicProperties.MessageId;
            ArgumentException.ThrowIfNullOrWhiteSpace(messageId, nameof(messageId));

            var correlationId = eventArgs.BasicProperties.CorrelationId;
            ArgumentException.ThrowIfNullOrWhiteSpace(correlationId, nameof(correlationId));

            var messageTypeName = eventArgs.BasicProperties.GetHeaderValue(nameof(message.MessageType));
            ArgumentException.ThrowIfNullOrWhiteSpace(messageTypeName, nameof(messageTypeName));
            var messageType = _typeResolver.Resolve(messageTypeName, throwOnError: true);

            var senderId = eventArgs.BasicProperties.GetHeaderValue(nameof(message.SenderId));
            ArgumentException.ThrowIfNullOrWhiteSpace(senderId, nameof(senderId));

            var createdAt = DateTimeOffset.Parse(eventArgs.BasicProperties.GetHeaderValue(nameof(message.CreatedAt)));

            if (!MessageEnvelope.TryCreate(
                    eventArgs.Body.Span,
                    messageId: messageId,
                    correlationId: correlationId,
                    createdAt,
                    messageType!,
                    senderId: senderId,
                    _serializer,
                    out message))
            {
                throw new ArgumentException("unable to parse outbox message.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "an exception has occured while decoding queue message from Exchange '{ExchangeName}'. Error: {ExceptionMessage}",
                eventArgs.Exchange,
                ex.Message);

            await channel.BasicRejectAsync(eventArgs.DeliveryTag, requeue: false);
            return null;
        }

        return message;
    }
}
