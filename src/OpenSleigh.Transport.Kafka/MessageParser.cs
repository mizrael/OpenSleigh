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
        private readonly ITransportSerializer _serializer;
        private readonly IQueueReferenceFactory _queueReferenceFactory;

        public MessageParser(ITransportSerializer serializer, IQueueReferenceFactory queueReferenceFactory)
        {     
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _queueReferenceFactory = queueReferenceFactory ?? throw new ArgumentNullException(nameof(queueReferenceFactory));
        }

        public IMessage Parse(ConsumeResult<Guid, byte[]> consumeResult)
        {
            if (consumeResult is null)
                throw new ArgumentNullException(nameof(consumeResult));
            
            var messageType = _queueReferenceFactory.GetQueueType(consumeResult.Topic);
            if(messageType is null) 
                throw new ArgumentException("invalid message type");

            var decodedObj = _serializer.Deserialize(consumeResult.Message.Value, messageType);
            if (decodedObj is not IMessage message)
                throw new ArgumentException($"message has the wrong type: '{messageType.FullName}'");
            return message;
        }
    }
}
