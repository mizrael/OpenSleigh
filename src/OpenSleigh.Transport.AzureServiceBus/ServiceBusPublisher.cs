using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Utils;

namespace OpenSleigh.Transport.AzureServiceBus
{
    internal class ServiceBusPublisher : IPublisher
    {
        private readonly IServiceBusSenderFactory _senderFactory;
        private readonly ISerializer _serializer;

        public ServiceBusPublisher(IServiceBusSenderFactory senderFactory, ISerializer serializer)
        {
            _senderFactory = senderFactory ?? throw new ArgumentNullException(nameof(senderFactory));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        public async Task PublishAsync(IMessage message, CancellationToken cancellationToken = default)
        {
            if (message == null) 
                throw new ArgumentNullException(nameof(message));
            
            var sender = _senderFactory.Create(message);

            var serializedMessage = await _serializer.SerializeAsync(message, cancellationToken);
            var busMessage = new ServiceBusMessage(serializedMessage)
            {
                CorrelationId = message.CorrelationId.ToString(), 
                MessageId = message.Id.ToString(),
                ApplicationProperties =
                {
                    { HeaderNames.MessageType, message.GetType().FullName }
                }
            };

            await sender.SendMessageAsync(busMessage, cancellationToken);
        }
    }
}
