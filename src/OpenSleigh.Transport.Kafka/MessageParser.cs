using Confluent.Kafka;
using OpenSleigh.Core;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Utils;
using System;
using System.Linq;
using System.Text;

namespace OpenSleigh.Transport.Kafka
{
    public class MessageParser : IMessageParser
    {
        private readonly ITypeResolver _typeResolver;
        private readonly ISerializer _serializer;

        public MessageParser(ITypeResolver typeResolver, ISerializer serializer)
        {
            _typeResolver = typeResolver ?? throw new ArgumentNullException(nameof(typeResolver));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        public IMessage Parse(ConsumeResult<Guid, byte[]> cr)
        {
            var messageTypeHeader = cr.Message.Headers.First(h => h.Key == HeaderNames.MessageType);
            var messageTypeName = Encoding.UTF8.GetString(messageTypeHeader.GetValueBytes());
            var messageType = _typeResolver.Resolve(messageTypeName);
            var decodedObj = _serializer.Deserialize(cr.Message.Value, messageType);
            if (decodedObj is not IMessage message)
                throw new ArgumentException($"message has the wrong type: '{messageTypeName}'");
            return message;
        }
    }
}
