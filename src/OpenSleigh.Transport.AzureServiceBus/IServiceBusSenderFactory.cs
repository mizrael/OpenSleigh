using Azure.Messaging.ServiceBus;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Transport.AzureServiceBus
{
    internal interface IServiceBusSenderFactory
    {
        ServiceBusSender Create<TM>(TM message = default) where TM : IMessage;
    }
}