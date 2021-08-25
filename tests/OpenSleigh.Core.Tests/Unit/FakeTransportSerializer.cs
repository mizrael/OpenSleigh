using OpenSleigh.Core.Utils;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Core.Tests.Unit
{
    public class FakeTransportSerializer : ITransportSerializer
    {
        public object Deserialize(ReadOnlyMemory<byte> data, Type type)
        {
            throw new NotImplementedException();
        }

        public ValueTask<T> DeserializeAsync<T>(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<byte[]> SerializeAsync<T>(T state, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}
