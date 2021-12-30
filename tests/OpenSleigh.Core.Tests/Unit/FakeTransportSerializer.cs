using OpenSleigh.Core.Utils;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Core.Tests.Unit
{
    public class FakeTransportSerializer : ITransportSerializer
    {
        public object Deserialize(byte[] data, Type type)
        {
            throw new NotImplementedException();
        }

        public ValueTask<T> DeserializeAsync<T>(Stream data, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public byte[] Serialize<T>(T data)
        {
            throw new NotImplementedException();
        }
    }
}
