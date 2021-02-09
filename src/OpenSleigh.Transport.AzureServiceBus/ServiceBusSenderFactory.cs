using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Transport.AzureServiceBus
{
    
    internal class ServiceBusSenderFactory : IAsyncDisposable, IServiceBusSenderFactory
    {
        private readonly IQueueReferenceFactory _queueReferenceFactory;
        private readonly ServiceBusClient _serviceBusClient;
        private readonly ConcurrentDictionary<QueueReferences, ServiceBusSender> _senders = new();
        
        public ServiceBusSenderFactory(IQueueReferenceFactory queueReferenceFactory, ServiceBusClient serviceBusClient)
        {
            _queueReferenceFactory = queueReferenceFactory ?? throw new ArgumentNullException(nameof(queueReferenceFactory));
            _serviceBusClient = serviceBusClient ?? throw new ArgumentNullException(nameof(serviceBusClient));
        }

        public ServiceBusSender Create<TM>(TM message = default) where TM : IMessage
        {
            var references = _queueReferenceFactory.Create<TM>();
            
            var sender = _senders.GetOrAdd(references, _ => _serviceBusClient.CreateSender(references.TopicName));
            if (sender.IsClosed)
                sender = _senders[references] = _serviceBusClient.CreateSender(references.TopicName);
         
            return sender;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var sender in _senders.Values)
                await sender.DisposeAsync();
        }
    }
}