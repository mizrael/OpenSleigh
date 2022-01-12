using System;

namespace OpenSleigh.Core.Utils
{
    public interface IPersistenceSerializer
    {
        byte[] Serialize<T>(T data);
        T Deserialize<T>(byte[] data);
        object Deserialize(byte[] data, Type type);
    }
}