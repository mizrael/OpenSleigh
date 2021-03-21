using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OpenSleigh.Core;
using OpenSleigh.Core.Exceptions;
using OpenSleigh.Core.Persistence;
using OpenSleigh.Core.Utils;

namespace OpenSleigh.Persistence.Cosmos.SQL
{
    [ExcludeFromCodeCoverage]
    public record CosmosSqlSagaStateRepositoryOptions(TimeSpan LockMaxDuration)
    {
        public static readonly CosmosSqlSagaStateRepositoryOptions Default = new (TimeSpan.FromMinutes(1));
    }
    
    public class CosmosSqlSagaStateRepository : ISagaStateRepository
    {
        private readonly ISagaDbContext _dbContext;
        private readonly ISerializer _serializer;
        private readonly CosmosSqlSagaStateRepositoryOptions _options;

        public CosmosSqlSagaStateRepository(ISagaDbContext dbContext, ISerializer serializer, CosmosSqlSagaStateRepositoryOptions options)
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
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.CorrelationId == correlationId && 
                                              e.Type == stateType.FullName, cancellationToken)
                    .ConfigureAwait(false); 
                
                if (stateEntity is null)
                {
                    resultState = newState;

                    var serializedState = await _serializer.SerializeAsync(newState, cancellationToken);
                    var newEntity = new Entities.SagaState(Guid.NewGuid().ToString(),
                                                           correlationId, stateType.FullName)
                    {
                        Data = serializedState,
                        LockId = lockId,
                        LockTime = DateTime.UtcNow
                    };
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

            return ReleaseLockAsyncCore(state, cancellationToken);
        }

        private async Task ReleaseLockAsyncCore<TD>(TD state, CancellationToken cancellationToken) where TD : SagaState
        {
            var stateType = typeof(TD);

            var stateEntity = await _dbContext.SagaStates
                .FirstOrDefaultAsync(e => e.CorrelationId == state.Id &&
                                          e.Type == stateType.FullName, cancellationToken)
                .ConfigureAwait(false);

            if (null == stateEntity)
                throw new LockException($"unable to find Saga State '{state.Id}'");

            stateEntity.LockTime = null;
            stateEntity.LockId = null;
            stateEntity.Data = await _serializer.SerializeAsync(state, cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
