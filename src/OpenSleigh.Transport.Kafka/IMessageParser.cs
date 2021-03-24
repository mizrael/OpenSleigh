using Confluent.Kafka;
using OpenSleigh.Core.Messaging;
using System;

namespace OpenSleigh.Transport.Kafka
{
    public interface IMessageParser
    {
        IMessage Parse(ConsumeResult<Guid, byte[]> consumeResult);
    }
}