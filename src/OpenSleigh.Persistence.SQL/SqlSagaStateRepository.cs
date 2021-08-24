using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OpenSleigh.Core;
using OpenSleigh.Core.Exceptions;
using OpenSleigh.Core.Persistence;
using OpenSleigh.Core.Utils;

namespace OpenSleigh.Persistence.SQL
{
    [ExcludeFromCodeCoverage]
    public record SqlSagaStateRepositoryOptions(TimeSpan LockMaxDuration)
    {
        public static readonly SqlSagaStateRepositoryOptions Default = new (TimeSpan.FromMinutes(1));
    }
    
    public class SqlSagaStateRepository : ISagaStateRepository
    {
        private readonly ISagaDbContext _dbContext;
        private readonly IPersistenceSerializer _serializer;
        private readonly SqlSagaStateRepositoryOptions _options;

        public SqlSagaStateRepository(ISagaDbContext dbContext, IPersistenceSerializer serializer, SqlSagaStateRepositoryOptions options)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task<(TD state, Guid lockId)> LockAsync<TD>(Guid correlationId, TD newState = default(TD), CancellationToken cancellationToken = default) where TD : SagaState
        {
            TD resultState; 
            var stateType = typeof(TD);
            var lockId = Guid.NewGuid();

            var transaction = await _dbContext.StartTransactionAsync(cancellationToken);

            try
            {
                var stateEntity = await _dbContext.SagaStates
                    .FirstOrDefaultAsync(e => e.CorrelationId == correlationId && 
                                              e.Type == stateType.FullName, cancellationToken)
                    .ConfigureAwait(false); 
                
                if (stateEntity is null)
                {
                    resultState = newState;

                    var serializedState = await _serializer.SerializeAsync(newState, cancellationToken);
                    var newEntity = new Entities.SagaState(correlationId, stateType.FullName, serializedState,
                                                            lockId, DateTime.UtcNow);
                    _dbContext.SagaStates.Add(newEntity);
                }
                else
                {
                    if (stateEntity.LockId.HasValue &&
                        stateEntity.LockTime.HasValue &&
                        stateEntity.LockTime.Value > DateTime.UtcNow - _options.LockMaxDuration)
                        throw new LockException($"saga state '{correlationId}' is already locked");
                    
                    stateEntity.LockTime = DateTime.UtcNow;
                    stateEntity.LockId = lockId;
                    
                    resultState = await _serializer.DeserializeAsync<TD>(stateEntity.Data, cancellationToken);
                }

                await _dbContext.SaveChangesAsync(cancellationToken)
                                .ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }

            return (resultState, lockId);
        }

        public Task ReleaseLockAsync<TD>(TD state, Guid lockId, 
            CancellationToken cancellationToken = default) where TD : SagaState
        {
            if (state == null) 
                throw new ArgumentNullException(nameof(state));

            return ReleaseLockAsyncCore(state, lockId, cancellationToken);
        }

        private async Task ReleaseLockAsyncCore<TD>(TD state, Guid lockId, CancellationToken cancellationToken) where TD : SagaState
        {
            var stateType = typeof(TD);

            var stateEntity = await _dbContext.SagaStates
                .FirstOrDefaultAsync(e => e.LockId == lockId &&
                                          e.CorrelationId == state.Id &&
                                          e.Type == stateType.FullName, cancellationToken)
                .ConfigureAwait(false);

            if (null == stateEntity)
                throw new LockException($"unable to release Saga State '{state.Id}' with type '{stateType.FullName}' by lock id {lockId}");

            stateEntity.LockTime = null;
            stateEntity.LockId = null;
            stateEntity.Data = await _serializer.SerializeAsync(state, cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
