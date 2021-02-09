using Azure.Messaging.ServiceBus;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Transport.AzureServiceBus
{
    internal interface IMessageParser
    {
        TM Resolve<TM>(ServiceBusReceivedMessage busMessage) where TM : IMessage;
    }
}