using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Utils;

namespace OpenSleigh.Transport.AzureServiceBus
{
    internal class ServiceBusPublisher : IPublisher
    {
        private readonly IServiceBusSenderFactory _senderFactory;
        private readonly ISerializer _serializer;
        private readonly ILogger<ServiceBusPublisher> _logger;
        private readonly SystemInfo _systemInfo;

        public ServiceBusPublisher(IServiceBusSenderFactory senderFactory,
            ISerializer serializer,
            ILogger<ServiceBusPublisher> logger, SystemInfo systemInfo)
        {
            _senderFactory = senderFactory ?? throw new ArgumentNullException(nameof(senderFactory));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));
        }

        public Task PublishAsync(IMessage message, CancellationToken cancellationToken = default)
        {
            if (message == null) 
                throw new ArgumentNullException(nameof(message));

            return PublishAsyncCore(message, cancellationToken);
        }

        private async Task PublishAsyncCore(IMessage message, CancellationToken cancellationToken)
        {
            ServiceBusSender sender = _senderFactory.Create((dynamic) message);
            _logger.LogInformation($"client '{_systemInfo.ClientId}' publishing message '{message.Id}' to {sender.FullyQualifiedNamespace}/{sender.EntityPath}");

            var serializedMessage = await _serializer.SerializeAsync(message, cancellationToken);
            var busMessage = new ServiceBusMessage(serializedMessage)
            {
                CorrelationId = message.CorrelationId.ToString(),
                MessageId = message.Id.ToString(),
                ApplicationProperties =
                {
                    {HeaderNames.MessageType, message.GetType().FullName}
                }
            };

            await sender.SendMessageAsync(busMessage, cancellationToken).ConfigureAwait(false);
        }
    }
}
