﻿using MongoDB.Bson;
using MongoDB.Driver;
using OpenSleigh.Transport;
using OpenSleigh.Utils;
using System.Diagnostics.CodeAnalysis;

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
        private readonly MongoSagaStateRepositoryOptions _options;
        private readonly ISerializer _serializer;

        public MongoSagaStateRepository(
            IDbContext dbContext, 
            MongoSagaStateRepositoryOptions? options, 
            ISerializer serializer)
        {
            _dbContext = dbContext;
            _options = options ?? MongoSagaStateRepositoryOptions.Default;
            _serializer = serializer;
        }

        private static ISagaExecutionContext<TS> CreateSagaContext<TS>(TS state, Entities.SagaState entity, SagaDescriptor descriptor)
            => new SagaExecutionContext<TS>(
                   instanceId: entity.InstanceId,
                   triggerMessageId: entity.TriggerMessageId,
                   correlationId: entity.CorrelationId,
                   descriptor: descriptor,
                   state: state,
                   processedMessages: entity.ProcessedMessages.Select(e => new ProcessedMessage()
                   {
                       MessageId = e.MessageId,
                       When = e.When
                   }));

        public async ValueTask<ISagaExecutionContext?> FindAsync(SagaDescriptor descriptor, string correlationId, CancellationToken cancellationToken = default)
        {
            var filterBuilder = Builders<Entities.SagaState>.Filter;

            var stateTypeFilter =
                descriptor.SagaStateType == null ?
                filterBuilder.Eq(e => e.SagaStateType, null) :
                filterBuilder.Eq(e => e.SagaStateType, descriptor.SagaStateType.FullName);

            var filter = filterBuilder.And(
                    filterBuilder.Eq(e => e.CorrelationId, correlationId),
                    filterBuilder.Eq(e => e.SagaType, descriptor.SagaType.FullName),
                    stateTypeFilter);

            var entity = await _dbContext.SagaStates.FindOneAsync(filter, cancellationToken)
                                         .ConfigureAwait(false);

            if (entity is null)
                return null;

            ISagaExecutionContext? result;

            if (descriptor.SagaStateType is null)
                result = new SagaExecutionContext(
                    instanceId: entity.InstanceId,
                    triggerMessageId: entity.TriggerMessageId,
                    correlationId: entity.CorrelationId,
                    descriptor: descriptor,
                    processedMessages: entity.ProcessedMessages.Select(e => new ProcessedMessage()
                    {
                        MessageId = e.MessageId,
                        When = e.When
                    }));
            else
            {
                var state = _serializer.Deserialize(entity.StateData, descriptor.SagaStateType);
                result = CreateSagaContext((dynamic)state, entity, descriptor);
            }

            if (entity.IsCompleted)
                result.MarkAsCompleted();

            return result;
        }

        public ValueTask<string> LockAsync(ISagaExecutionContext state, CancellationToken cancellationToken = default)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            return LockAsyncCore(state, cancellationToken);
        }

        private async ValueTask<string> LockAsyncCore(ISagaExecutionContext state, CancellationToken cancellationToken)
        {
            var lockId = Guid.NewGuid().ToString();

            var filterBuilder = Builders<Entities.SagaState>.Filter;
            var filter = filterBuilder.Eq(e => e.InstanceId, state.InstanceId);
            var entity = await _dbContext.SagaStates.FindOneAsync(filter, cancellationToken)
                                                    .ConfigureAwait(false);

            if (entity is null)
            {
                entity = new Entities.SagaState()
                {
                    Id = ObjectId.GenerateNewId(),
                    CorrelationId = state.CorrelationId,
                    InstanceId = state.InstanceId,
                    IsCompleted = state.IsCompleted,
                    SagaType = state.Descriptor.SagaType.FullName,
                    SagaStateType = state.Descriptor.SagaStateType?.FullName,
                    TriggerMessageId = state.TriggerMessageId,
                    LockId = lockId,
                    LockTime = DateTimeOffset.UtcNow,
                };
            }
            else
            {
                if (entity.LockId is not null &&
                    entity.LockTime is not null &&
                    entity.LockTime > DateTimeOffset.UtcNow - _options.LockMaxDuration)
                    throw new LockException($"saga state '{state.InstanceId}' is already locked");
            }

            entity.LockTime = DateTimeOffset.UtcNow;
            entity.LockId = lockId;

            await _dbContext.SagaStates.ReplaceOneAsync(filter, entity, new ReplaceOptions()
            {
                IsUpsert = true,                
            }).ConfigureAwait(false);

            return entity.LockId;
        }

        public ValueTask ReleaseAsync(ISagaExecutionContext state, string lockId, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}