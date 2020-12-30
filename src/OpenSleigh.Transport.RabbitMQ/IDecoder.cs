using System;

namespace OpenSleigh.Transport.RabbitMQ
{
    public interface IDecoder
    {
        object Decode(ReadOnlyMemory<byte> data, Type type);
        T Decode<T>(ReadOnlyMemory<byte> data);
    }
}