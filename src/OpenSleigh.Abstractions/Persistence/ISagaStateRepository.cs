using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Core.Persistence
{
    //TODO: this should use an EventSourcing-ish approach
    public interface ISagaStateRepository
    {
        Task<(TD state, Guid lockId)> LockAsync<TD>(Guid correlationId, TD newState = null, CancellationToken cancellationToken = default) where TD : SagaState;
        Task ReleaseLockAsync<TD>(TD state, Guid lockId,  CancellationToken cancellationToken = default) where TD : SagaState;
    }
}