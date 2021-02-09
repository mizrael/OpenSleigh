using System;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Transport.AzureServiceBus
{
    internal interface IMessageParser
    {
        TM Resolve<TM>(BinaryData messageData) where TM : IMessage;
    }
}