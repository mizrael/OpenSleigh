using System;
using Azure.Messaging.ServiceBus;
using OpenSleigh.Core;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Utils;

namespace OpenSleigh.Transport.AzureServiceBus
{
    internal class MessageParser : IMessageParser
    {
        private readonly ISerializer _decoder;

        public MessageParser(ISerializer encoder)
        {
            _decoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        }

        public TM Resolve<TM>(ServiceBusReceivedMessage busMessage) where TM : IMessage
        {
            if (busMessage is null)
                throw new ArgumentNullException(nameof(busMessage));
          
            var body = busMessage.Body.ToMemory();
            
            var deserializedObj = _decoder.Deserialize(body, typeof(TM));
            if (deserializedObj is not TM message)
                throw new ArgumentException($"unable to deserialize message '{busMessage.MessageId}' into '{typeof(TM).FullName}'");
            return message;
        }
        
    }
}