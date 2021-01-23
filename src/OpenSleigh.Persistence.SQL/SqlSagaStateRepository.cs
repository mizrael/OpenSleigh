using System;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core;
using OpenSleigh.Core.Persistence;

namespace OpenSleigh.Persistence.SQL
{
    public class SqlSagaStateRepository : ISagaStateRepository
    {
        private readonly ISagaDbContext _fixtureDbContext;

        public SqlSagaStateRepository(ISagaDbContext fixtureDbContext)
        {
            _fixtureDbContext = fixtureDbContext ?? throw new ArgumentNullException(nameof(fixtureDbContext));
        }

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
