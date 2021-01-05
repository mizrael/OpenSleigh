using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Persistence.Mongo.Utils
{
    public interface ISerializer
    {
        ValueTask<byte[]> SerializeAsync<T>(T state, CancellationToken cancellationToken = default);
        ValueTask<T> DeserializeAsync<T>(byte[] data, CancellationToken cancellationToken = default);
    }
}