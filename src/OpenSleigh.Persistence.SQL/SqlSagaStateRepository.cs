using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core;
using OpenSleigh.Core.Persistence;
using OpenSleigh.Core.Utils;

namespace OpenSleigh.Persistence.SQL
{
    public class SqlSagaStateRepository : ISagaStateRepository
    {
        private readonly ISagaDbContext _dbContext;
        private readonly ISerializer _serializer;

        public SqlSagaStateRepository(ISagaDbContext dbContext, ISerializer serializer)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        public async Task<(TD state, Guid lockId)> LockAsync<TD>(Guid correlationId, TD newState = default(TD), CancellationToken cancellationToken = default) where TD : SagaState
        {
            var stateType = typeof(TD);

            TD resultState = default;
            var lockId = Guid.NewGuid();

            var transaction = await _dbContext.StartTransactionAsync(cancellationToken);
            try{
                var oldState = await _dbContext.SagaStates.FindAsync(correlationId, stateType.FullName);
                if(oldState is null){
                    resultState = newState;
                    
                    var serializedState = await _serializer.SerializeAsync(newState, cancellationToken);
                    var newEntity = new Entities.SagaState(correlationId, stateType.FullName, serializedState, 
                                                            lockId, DateTime.UtcNow);
                    _dbContext.SagaStates.Add(newEntity);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }catch{
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }

            return (resultState, lockId);
        }

        public Task ReleaseLockAsync<TD>(TD state, Guid lockId, ITransaction transaction = null,
            CancellationToken cancellationToken = default) where TD : SagaState
        {
            throw new NotImplementedException();
        }
    }
}
