using OpenSleigh.Core;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Persistence.Mongo
{
    public interface ISagaStateSerializer
    {
        Task<byte[]> SerializeAsync<TD>(TD state, CancellationToken cancellationToken = default) where TD : SagaState;
        Task<TD> DeserializeAsync<TD>(byte[] data, CancellationToken cancellationToken = default) where TD : SagaState;
    }
}