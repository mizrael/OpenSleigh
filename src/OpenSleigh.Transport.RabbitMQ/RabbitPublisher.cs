using Microsoft.Extensions.Logging;
using Polly;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Transport.RabbitMQ
{
    
    public class RabbitPublisher : IPublisher
    {
        private readonly IPublisherChannelFactory _publisherChannelFactory;
        private readonly ILogger<RabbitPublisher> _logger;
        private readonly IEncoder _encoder;
        
        public RabbitPublisher(
            IEncoder encoder,
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

            using var context = _publisherChannelFactory.Create(message);

            var encodedMessage = _encoder.Encode(message);

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
                    body: encodedMessage.Value);

                _logger.LogInformation("message '{MessageId}' published to Exchange '{ExchangeName}', Queue '{QueueName}'", 
                    message.Id,
                    context.QueueReferences.ExchangeName,
                    context.QueueReferences.QueueName);
            });
            
            return Task.CompletedTask;
        }
    }
}