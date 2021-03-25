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
#if !DEBUG
            // calling DisposeAsync() on each processor might take a long time to complete (~60sec) due to 
            // apparent limitations of the underlying AMQP library.
            // more details here: https://github.com/Azure/azure-sdk-for-net/issues/19306
            // Therefore we do it only when in Release mode. Debug mode is used when running the tests suite.

            foreach (var sender in _processors.Values)
            {
                try
                {                    
                    await sender.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
#endif              
            _processors.Clear();
        }
    }
}