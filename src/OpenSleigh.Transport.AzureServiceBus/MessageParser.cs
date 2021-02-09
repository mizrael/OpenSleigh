using System;
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

        public TM Resolve<TM>(BinaryData messageData) where TM : IMessage
        {
            if (messageData is null)
                throw new ArgumentNullException(nameof(messageData));

            return (TM)_decoder.Deserialize(messageData, typeof(TM));
        }
    }
}