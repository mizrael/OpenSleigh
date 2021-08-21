using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Core.Utils
{
    public interface ITransportSerializer
    {        
        ValueTask<ReadOnlyMemory<byte>> SerializeAsync<T>(T state, CancellationToken cancellationToken = default);
        ValueTask<T> DeserializeAsync<T>(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
        object Deserialize(ReadOnlyMemory<byte> data, Type type); //TODO: not super happy of this one.
    }
}