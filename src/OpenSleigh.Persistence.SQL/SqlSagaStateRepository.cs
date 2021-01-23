using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core;
using OpenSleigh.Core.Persistence;

namespace OpenSleigh.Persistence.SQL
{
    internal class SqlSagaStateRepository : ISagaStateRepository
    {
        public Task<(TD state, Guid lockId)> LockAsync<TD>(Guid correlationId, TD newState = default(TD), CancellationToken cancellationToken = default) where TD : SagaState
        {
            throw new NotImplementedException();
        }

        public Task ReleaseLockAsync<TD>(TD state, Guid lockId, ITransaction transaction = null,
            CancellationToken cancellationToken = default) where TD : SagaState
        {
            throw new NotImplementedException();
        }
    }
}
