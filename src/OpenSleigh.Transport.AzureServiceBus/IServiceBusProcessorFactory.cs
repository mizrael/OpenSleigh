using Azure.Messaging.ServiceBus;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Transport.AzureServiceBus
{
    internal interface IServiceBusProcessorFactory
    {
        ServiceBusProcessor Create<TM>() where TM : IMessage;
    }
}