using OpenSleigh.Core;
using RabbitMQ.Client;
using System;
using System.Text;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Transport.RabbitMQ
{
    public class MessageParser : IMessageParser
    {
        private readonly IDecoder _decoder;
        private readonly ITypeResolver _typeResolver;
        
        public MessageParser(IDecoder encoder, ITypeResolver typeResolver)
        {
            _decoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
            _typeResolver = typeResolver ?? throw new ArgumentNullException(nameof(typeResolver));
        }

        public IMessage Resolve(IBasicProperties basicProperties, ReadOnlyMemory<byte> body)
        {
            if (basicProperties is null)
                throw new ArgumentNullException(nameof(basicProperties));
            if (basicProperties.Headers is null)
                throw new ArgumentNullException(nameof(basicProperties), "message headers are missing");

            if (!basicProperties.Headers.TryGetValue(HeaderNames.MessageType, out var tmp) ||
                tmp is not byte[] messageTypeBytes ||
                messageTypeBytes is null)
                throw new ArgumentException("invalid message type");

            var messageTypeName = Encoding.UTF8.GetString(messageTypeBytes);

            var dataType = _typeResolver.Resolve(messageTypeName);

            var decodedObj = _decoder.Decode(body, dataType);
            if (decodedObj is not IMessage message)
                throw new ArgumentException($"message has the wrong type: '{messageTypeName}'");
            return message;
        }
    }
}