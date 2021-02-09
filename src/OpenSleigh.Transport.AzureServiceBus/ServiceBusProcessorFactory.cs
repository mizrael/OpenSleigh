using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Transport.AzureServiceBus
{
    internal class ServiceBusProcessorFactory : IAsyncDisposable, IServiceBusProcessorFactory
    {
        private readonly IQueueReferenceFactory _queueReferenceFactory;
        private readonly ServiceBusClient _serviceBusClient;
        private readonly ConcurrentDictionary<QueueReferences, ServiceBusProcessor> _processors = new();

        public ServiceBusProcessorFactory(IQueueReferenceFactory queueReferenceFactory, ServiceBusClient serviceBusClient)
        {
            _queueReferenceFactory = queueReferenceFactory ?? throw new ArgumentNullException(nameof(queueReferenceFactory));
            _serviceBusClient = serviceBusClient ?? throw new ArgumentNullException(nameof(serviceBusClient));
        }

        public ServiceBusProcessor Create<TM>() where TM : IMessage
        {
            var references = _queueReferenceFactory.Create<TM>();

            var processor = _processors.GetOrAdd(references, _ => _serviceBusClient.CreateProcessor(references.TopicName, references.SubscriptionName));
            if (processor is null || processor.IsClosed)
                processor = _processors[references] = _serviceBusClient.CreateProcessor(references.TopicName, references.SubscriptionName);

            return processor;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var sender in _processors.Values)
                await sender.CloseAsync().ConfigureAwait(false);
            _processors.Clear();
        }
    }
}