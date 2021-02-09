using Azure.Messaging.ServiceBus;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Transport.AzureServiceBus
{
    internal interface IMessageParser
    {
        TM Resolve<TM>(ServiceBusReceivedMessage message) where TM : IMessage;
    }
}