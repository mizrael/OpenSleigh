using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core;
using OpenSleigh.Core.Persistence;

namespace OpenSleigh.Persistence.SQL
{
    public class SqlSagaStateRepository : ISagaStateRepository
    {
        private readonly ISagaDbContext _dbContext;

        public SqlSagaStateRepository(ISagaDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public async Task<(TD state, Guid lockId)> LockAsync<TD>(Guid correlationId, TD newState = default(TD), CancellationToken cancellationToken = default) where TD : SagaState
        {
            var stateType = typeof(TD);

            var transaction = await _dbContext.StartTransactionAsync(cancellationToken);
            try{
                var oldState = await _dbContext.SagaStates.FindAsync(correlationId, stateType.FullName);
                if(oldState is null){
                    
                }
            }catch{
                await transaction.RollbackAsync(cancellationToken);
            }

            throw new NotImplementedException();
        }

        public Task ReleaseLockAsync<TD>(TD state, Guid lockId, ITransaction transaction = null,
            CancellationToken cancellationToken = default) where TD : SagaState
        {
            throw new NotImplementedException();
        }
    }
}
