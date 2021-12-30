using OpenSleigh.Core.Utils;
using System;

namespace OpenSleigh.Core.Tests.Unit
{
    public class FakePersistenceSerializer : IPersistenceSerializer
    {
        public T Deserialize<T>(byte[] data)
        {
            throw new NotImplementedException();
        }

        public object Deserialize(byte[] data, Type type)
        {
            throw new NotImplementedException();
        }

        public byte[] Serialize<T>(T data)
        {
            throw new NotImplementedException();
        }
    }
}
