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
            {
                try
                {
                    // this call might take a long time to complete (~60sec) due to 
                    // apparent limitations of the underlying AMQP library.
                    // more details here: https://github.com/Azure/azure-sdk-for-net/issues/19306
                    await sender.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
                
            _processors.Clear();
        }
    }
}