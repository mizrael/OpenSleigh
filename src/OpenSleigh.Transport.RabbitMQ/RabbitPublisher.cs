using Microsoft.Extensions.Logging;
using OpenSleigh.Outbox;
using OpenSleigh.Utils;
using Polly;

namespace OpenSleigh.Transport.RabbitMQ
{
    public class RabbitPublisher : IPublisher
    {
        private readonly IQueueReferenceFactory _queueReferenceFactory;
        private readonly ILogger<RabbitPublisher> _logger;
        private readonly ISerializer _encoder;
        private readonly IChannelFactory _channelFactory;

        public RabbitPublisher(
            ISerializer encoder,
            ILogger<RabbitPublisher> logger,
            IQueueReferenceFactory queueReferenceFactory,
            IChannelFactory channelFactory)
        {
            _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _queueReferenceFactory = queueReferenceFactory ?? throw new ArgumentNullException(nameof(queueReferenceFactory));
            _channelFactory = channelFactory ?? throw new ArgumentNullException(nameof(channelFactory));
        }

        public ValueTask PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            if (message is null)
                throw new ArgumentNullException(nameof(message));

            var queueRef = _queueReferenceFactory.Create(message);
            var channel = _channelFactory.Get(queueRef);
            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.MessageId = message.MessageId;
            properties.CorrelationId = message.CorrelationId;
            properties.Headers = new Dictionary<string, object>()
            {
                { nameof(message.MessageType), message.MessageType.FullName },                
                { nameof(message.ParentId), message.ParentId ?? string.Empty },
                { nameof(message.SenderId), message.SenderId ?? string.Empty },
                { nameof(message.CreatedAt), message.CreatedAt.ToString() }
            };

            var policy = Policy.Handle<Exception>()
                .WaitAndRetry(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), (ex, time) =>
                {
                    _logger.LogWarning(ex,
                        "Could not publish message '{MessageId}' to Exchange '{ExchangeName}', after {Timeout}s : {ExceptionMessage}",
                        message.MessageId,
                        queueRef.ExchangeName,
                        $"{time.TotalSeconds:n1}", ex.Message);
                });

            policy.Execute(() =>
            {
                channel.BasicPublish(
                    exchange: queueRef.ExchangeName,
                    routingKey: queueRef.RoutingKey,
                    mandatory: true,
                    basicProperties: properties,
                    body: message.Body);

                _logger.LogInformation("message '{MessageId}' published to Exchange '{ExchangeName}'",
                    message.MessageId,
                    queueRef.ExchangeName);
            });

            return ValueTask.CompletedTask;
        }
    }
}