using OpenSleigh.Core.Messaging;
using RabbitMQ.Client;

namespace OpenSleigh.Transport.RabbitMQ
{
    public interface IMessageParser
    {
        IMessage Resolve(IBasicProperties basicProperties, byte[] body);
    }
}