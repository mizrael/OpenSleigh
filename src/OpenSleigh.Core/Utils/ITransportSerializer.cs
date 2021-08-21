using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Core.Utils
{
    public interface ITransportSerializer
    {        
        ValueTask<ReadOnlyMemory<byte>> SerializeAsync<T>(T state, CancellationToken cancellationToken = default);
        ValueTask<T> DeserializeAsync<T>(ReadOnlySpan<byte> data, CancellationToken cancellationToken = default);
        object Deserialize(ReadOnlySpan<byte> data, Type type); //TODO: not super happy of this one.
    }
}