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
        private readonly ITransportSerializer _serializer;

        public MessageParser(ITypeResolver typeResolver, ITransportSerializer serializer)
        {
            _typeResolver = typeResolver ?? throw new ArgumentNullException(nameof(typeResolver));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        public IMessage Parse(ConsumeResult<Guid, byte[]> consumeResult)
        {
            if (consumeResult is null)
                throw new ArgumentNullException(nameof(consumeResult));
            if (consumeResult.Message?.Headers is null)
                throw new ArgumentNullException(nameof(consumeResult), "message headers are missing");

            var messageTypeHeader = consumeResult.Message.Headers.FirstOrDefault(h => h.Key == HeaderNames.MessageType);
            if(messageTypeHeader is null)
                throw new ArgumentException("invalid message type");

            var messageTypeName = Encoding.UTF8.GetString(messageTypeHeader.GetValueBytes());
            var messageType = _typeResolver.Resolve(messageTypeName);
            var decodedObj = _serializer.Deserialize(consumeResult.Message.Value, messageType);
            if (decodedObj is not IMessage message)
                throw new ArgumentException($"message has the wrong type: '{messageTypeName}'");
            return message;
        }
    }
}
