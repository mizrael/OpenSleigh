using RabbitMQ.Client;
using System;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Transport.RabbitMQ
{
    public interface IMessageParser
    {
        IMessage Resolve(IBasicProperties basicProperties, ReadOnlyMemory<byte> body);
    }
}