using Microsoft.Extensions.Logging;
using OpenSleigh.Outbox;
using Polly;
using RabbitMQ.Client;

namespace OpenSleigh.Transport.RabbitMQ;

public class RabbitPublisher : IPublisher
{
    private readonly IQueueReferenceFactory _queueReferenceFactory;
    private readonly ILogger<RabbitPublisher> _logger;
    private readonly IChannelFactory _channelFactory;

    public RabbitPublisher(
        IQueueReferenceFactory queueReferenceFactory,
        IChannelFactory channelFactory,
        ILogger<RabbitPublisher> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _queueReferenceFactory = queueReferenceFactory ?? throw new ArgumentNullException(nameof(queueReferenceFactory));
        _channelFactory = channelFactory ?? throw new ArgumentNullException(nameof(channelFactory));
    }

    public async ValueTask PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var queueRef = _queueReferenceFactory.Create(message);
        var channel = await _channelFactory.GetAsync(queueRef, cancellationToken);
        var properties = new BasicProperties();
        properties.Persistent = true;
        properties.MessageId = message.MessageId;
        properties.CorrelationId = message.CorrelationId;
        properties.Headers = new Dictionary<string, object?>()
        {
            { nameof(message.MessageType), message.MessageType.FullName },                
            { nameof(message.ParentId), message.ParentId ?? string.Empty },
            { nameof(message.SenderId), message.SenderId },
            { nameof(message.CreatedAt), message.CreatedAt.ToString() }
        };

        var policy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), (ex, time) =>
            {
                _logger.LogWarning(ex,
                    "Could not publish message '{MessageId}' to Exchange '{ExchangeName}', after {Timeout}s : {ExceptionMessage}",
                    message.MessageId,
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
                body: message.Body);

            _logger.LogInformation("message '{MessageId}' published to Exchange '{ExchangeName}'",
                message.MessageId,
                queueRef.ExchangeName);
        });
    }
}