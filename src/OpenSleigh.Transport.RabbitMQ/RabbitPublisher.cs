using Microsoft.Extensions.Logging;
using Polly;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Utils;

namespace OpenSleigh.Transport.RabbitMQ
{
    
    public class RabbitPublisher : IPublisher
    {
        private readonly IPublisherChannelFactory _publisherChannelFactory;
        private readonly ILogger<RabbitPublisher> _logger;
        private readonly ISerializer _encoder;
        
        public RabbitPublisher(
            ISerializer encoder,
            ILogger<RabbitPublisher> logger, 
            IPublisherChannelFactory publisherChannelFactory)
        {
            _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _publisherChannelFactory = publisherChannelFactory ?? throw new ArgumentNullException(nameof(publisherChannelFactory));
        }

        public Task PublishAsync(IMessage message, CancellationToken cancellationToken = default)
        {
            if (message is null)
                throw new ArgumentNullException(nameof(message));

            return PublishAsyncCore(message, cancellationToken);
        }

        private async Task PublishAsyncCore(IMessage message, CancellationToken cancellationToken)
        {
            using var context = _publisherChannelFactory.Create(message);

            var encodedMessage = await _encoder.SerializeAsync(message, cancellationToken);

            var properties = context.Channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.Headers = new Dictionary<string, object>()
            {
                {HeaderNames.MessageType, message.GetType().FullName}
            };

            var policy = Policy.Handle<Exception>()
                .WaitAndRetry(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), (ex, time) =>
                {
                    _logger.LogWarning(ex,
                        "Could not publish message '{MessageId}' to Exchange '{ExchangeName}', Queue '{QueueName}' after {Timeout}s : {ExceptionMessage}",
                        message.Id,
                        context.QueueReferences.ExchangeName,
                        context.QueueReferences.QueueName,
                        $"{time.TotalSeconds:n1}", ex.Message);
                });

            policy.Execute(() =>
            {
                context.Channel.BasicPublish(
                    exchange: context.QueueReferences.ExchangeName,
                    routingKey: context.QueueReferences.QueueName,
                    mandatory: true,
                    basicProperties: properties,
                    body: encodedMessage);

                _logger.LogInformation("message '{MessageId}' published to Exchange '{ExchangeName}', Queue '{QueueName}'",
                    message.Id,
                    context.QueueReferences.ExchangeName,
                    context.QueueReferences.QueueName);
            });
        }
    }
}