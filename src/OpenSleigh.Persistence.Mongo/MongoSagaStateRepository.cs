using OpenSleigh.Core;
using OpenSleigh.Core.Exceptions;
using OpenSleigh.Core.Persistence;
using MongoDB.Driver;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Persistence.Mongo
{
    public record MongoSagaStateRepositoryOptions(TimeSpan LockMaxDuration)
    {
        public static readonly MongoSagaStateRepositoryOptions Default = new MongoSagaStateRepositoryOptions(TimeSpan.FromMinutes(1));
    }

    public class MongoSagaStateRepository : ISagaStateRepository
    {
        private readonly IDbContext _dbContext;
        private readonly ISagaStateSerializer _sagaStateSerializer;
        private readonly MongoSagaStateRepositoryOptions _options;
        
        public MongoSagaStateRepository(IDbContext dbContext, ISagaStateSerializer sagaStateSerializer, MongoSagaStateRepositoryOptions options)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _sagaStateSerializer = sagaStateSerializer ?? throw new ArgumentNullException(nameof(sagaStateSerializer));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task<(TD state, Guid lockId)> LockAsync<TD>(Guid correlationId, TD newEntity = null, CancellationToken cancellationToken = default)
            where TD : SagaState
        {
            var stateType = typeof(TD);
            
            var filterBuilder = Builders<Entities.SagaState>.Filter;
            var filter = filterBuilder.And(
                filterBuilder.Eq(e => e.CorrelationId, correlationId),
                filterBuilder.Eq(e => e.Type, stateType.FullName),
                filterBuilder.Or(
                    filterBuilder.Eq(e => e.LockId, null),
                    filterBuilder.Lt(e => e.LockTime, DateTime.UtcNow - _options.LockMaxDuration)
                )
            );
            var update = Builders<Entities.SagaState>.Update
                .Set(e => e.LockId, Guid.NewGuid())
                .Set(e => e.LockTime, DateTime.UtcNow);

            if (newEntity is not null)
            {
                var serializedState = await _sagaStateSerializer.SerializeAsync(newEntity, cancellationToken);
                
                update = update.SetOnInsert(e => e.CorrelationId, correlationId)
                    .SetOnInsert(e => e.Type, stateType.FullName)
                    .SetOnInsert(e => e.Data, serializedState);
            }

            var options = new FindOneAndUpdateOptions<Entities.SagaState>()
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.After
            };

            try
            {
                var entity = await _dbContext.SagaStates
                    .FindOneAndUpdateAsync(filter, update, options, cancellationToken)
                    .ConfigureAwait(false);
                if (entity is null)
                    return (null, Guid.Empty);

                // can't deserialize a BsonDocument to <TD> so we have to use JSON instead
                var state = await _sagaStateSerializer.DeserializeAsync<TD>(entity.Data, cancellationToken);
                return (state, entity.LockId.Value);
            }
            catch (MongoCommandException e) when (e.Code == 11000 && e.CodeName == "DuplicateKey")
            {
                throw new LockException($"saga state '{correlationId}' is already locked");
            }
        }

        public async Task UpdateAsync<TD>(TD state, Guid lockId, bool releaseLock = false, CancellationToken cancellationToken = default)
            where TD : SagaState
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            var serializedState = await _sagaStateSerializer.SerializeAsync(state, cancellationToken);
            var stateType = typeof(TD);

            var filterBuilder = Builders<Entities.SagaState>.Filter;
            var filter = filterBuilder.And(
                filterBuilder.Eq(e => e.CorrelationId, state.Id),
                filterBuilder.Eq(e => e.Type, stateType.FullName),
                filterBuilder.Eq(e => e.LockId, lockId)
            );

            var update = Builders<Entities.SagaState>.Update
                  .Set(e => e.Data, serializedState);
            if (releaseLock)
                update = update.Set(e => e.LockId, null)
                                .Set(e => e.LockTime, null);

            var options = new UpdateOptions()
            {
                IsUpsert = true
            };

            var result = await _dbContext.SagaStates.UpdateOneAsync(filter, update, options, cancellationToken)
                .ConfigureAwait(false);

            var failed = (result is null || result.MatchedCount == 0);
            if (failed)
            {
                if (releaseLock)
                    throw new LockException($"unable to release lock on saga state '{state.Id}'");
                else
                    throw new Exception($"unable to update saga state '{state.Id}'");
            }
        }
    }
}