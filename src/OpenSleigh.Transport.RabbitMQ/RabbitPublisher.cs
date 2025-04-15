using Microsoft.Extensions.Logging;
using OpenSleigh.Outbox;
using OpenSleigh.Utils;
using Polly;
using RabbitMQ.Client;

namespace OpenSleigh.Transport.RabbitMQ;

public class RabbitPublisher : IPublisher
{
    private readonly IQueueReferenceFactory _queueReferenceFactory;
    private readonly ILogger<RabbitPublisher> _logger;
    private readonly IChannelFactory _channelFactory;
    private readonly ISerializer _serializer;

    public RabbitPublisher(
        IQueueReferenceFactory queueReferenceFactory,
        IChannelFactory channelFactory,
        ILogger<RabbitPublisher> logger,
        ISerializer serializer)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _queueReferenceFactory = queueReferenceFactory ?? throw new ArgumentNullException(nameof(queueReferenceFactory));
        _channelFactory = channelFactory ?? throw new ArgumentNullException(nameof(channelFactory));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    public async ValueTask PublishAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var queueRef = _queueReferenceFactory.Create(envelope);
        var channel = await _channelFactory.GetAsync(queueRef, cancellationToken);
        var properties = new BasicProperties();
        properties.Persistent = true;
        properties.MessageId = envelope.MessageId;
        properties.CorrelationId = envelope.CorrelationId;
        properties.Headers = new Dictionary<string, object?>()
        {
            { nameof(envelope.MessageType), envelope.MessageType.FullName },                
            { nameof(envelope.ParentId), envelope.ParentId ?? string.Empty },
            { nameof(envelope.SenderId), envelope.SenderId },
            { nameof(envelope.CreatedAt), envelope.CreatedAt.ToString() }
        };

        var body = _serializer.Serialize(envelope.Message);

        var policy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), (ex, time) =>
            {
                _logger.LogWarning(ex,
                    "Could not publish message '{MessageId}' to Exchange '{ExchangeName}', after {Timeout}s : {ExceptionMessage}",
                    envelope.MessageId,
                    queueRef.ExchangeName,
                    $"{time.TotalSeconds:n1}", ex.Message);
            });

        await policy.ExecuteAsync(async () =>
        {
            await channel.BasicPublishAsync(
                exchange: queueRef.ExchangeName,
                routingKey: queueRef.RoutingKey,
                mandatory: true,
                basicProperties: properties,
                body: body);

            _logger.LogInformation("message '{MessageId}' published to Exchange '{ExchangeName}'",
                envelope.MessageId,
                queueRef.ExchangeName);
        });
    }
}