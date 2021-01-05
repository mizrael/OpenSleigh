using RabbitMQ.Client;
using System;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Transport.RabbitMQ
{
    public interface IMessageParser
    {
        TM Resolve<TM>(IBasicProperties basicProperties, ReadOnlyMemory<byte> body) where TM : IMessage;
    }
}