using OpenSleigh.Core;
using OpenSleigh.Core.Exceptions;
using OpenSleigh.Core.Persistence;
using MongoDB.Driver;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core.Utils;

namespace OpenSleigh.Persistence.Mongo
{
    [ExcludeFromCodeCoverage]
    public record MongoSagaStateRepositoryOptions(TimeSpan LockMaxDuration)
    {
        public static readonly MongoSagaStateRepositoryOptions Default = new MongoSagaStateRepositoryOptions(TimeSpan.FromMinutes(1));
    }
    
    public class MongoSagaStateRepository : ISagaStateRepository
    {
        private readonly IDbContext _dbContext;
        private readonly ISerializer _serializer;
        private readonly MongoSagaStateRepositoryOptions _options;
        
        public MongoSagaStateRepository(IDbContext dbContext, ISerializer serializer, MongoSagaStateRepositoryOptions options)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task<(TD state, Guid lockId)> LockAsync<TD>(Guid correlationId, TD newState = null, CancellationToken cancellationToken = default)
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

            if (newState is not null)
            {
                var serializedState = await _serializer.SerializeAsync(newState, cancellationToken);
                
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
                var state = await _serializer.DeserializeAsync<TD>(entity.Data, cancellationToken);
                return (state, entity.LockId.Value);
            }
            catch (MongoCommandException e) when (e.Code == 11000 && e.CodeName == "DuplicateKey")
            {
                throw new LockException($"saga state '{correlationId}' is already locked");
            }
        }

        public Task ReleaseLockAsync<TD>(TD state, Guid lockId, CancellationToken cancellationToken = default)
            where TD : SagaState
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            return ReleaseLockAsyncCore(state, lockId, cancellationToken);
        }

        private async Task ReleaseLockAsyncCore<TD>(TD state, Guid lockId, CancellationToken cancellationToken) where TD : SagaState
        {
            var serializedState = await _serializer.SerializeAsync(state, cancellationToken);
            var stateType = typeof(TD);

            var filterBuilder = Builders<Entities.SagaState>.Filter;
            var filter = filterBuilder.And(
                filterBuilder.Eq(e => e.CorrelationId, state.Id),
                filterBuilder.Eq(e => e.Type, stateType.FullName),
                filterBuilder.Eq(e => e.LockId, lockId)
            );

            var update = Builders<Entities.SagaState>.Update
                .Set(e => e.Data, serializedState)
                .Set(e => e.LockId, null)
                .Set(e => e.LockTime, null);

            var options = new UpdateOptions()
            {
                IsUpsert = false
            };

            UpdateResult result = null;
            if (_dbContext.Transaction?.Session is not null)
                result = await _dbContext.SagaStates.UpdateOneAsync(_dbContext.Transaction.Session, filter, update, options,
                        cancellationToken)
                    .ConfigureAwait(false);
            else
                result = await _dbContext.SagaStates.UpdateOneAsync(filter, update, options, cancellationToken)
                    .ConfigureAwait(false);

            var failed = (result is null || result.MatchedCount == 0);
            if (failed)
                throw new LockException($"unable to release lock on saga state '{state.Id}'");
        }
    }
}