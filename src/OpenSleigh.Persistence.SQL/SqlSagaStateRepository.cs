﻿using Microsoft.EntityFrameworkCore;
using OpenSleigh.Messaging;
using OpenSleigh.Persistence.SQL.Entities;
using OpenSleigh.Utils;
using System.Diagnostics.CodeAnalysis;

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
        private readonly SqlSagaStateRepositoryOptions _options;
        private readonly ISerializer _serializer;

        public SqlSagaStateRepository(ISagaDbContext dbContext, SqlSagaStateRepositoryOptions options, ISerializer serializer)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        public async ValueTask<ISagaExecutionContext?> FindAsync(SagaDescriptor descriptor, string correlationId, CancellationToken cancellationToken = default)
        {
            var entity = await _dbContext.SagaStates.FirstOrDefaultAsync(e =>
                e.CorrelationId == correlationId && 
                e.SagaType == descriptor.SagaType.FullName &&
                ((descriptor.SagaStateType == null && e.SagaStateType == null)|| 
                (e.SagaStateType == descriptor.SagaStateType.FullName)),
                cancellationToken).ConfigureAwait(false);

            if (entity is null)
                return null;

            if (descriptor.SagaStateType is null)            
                return new SagaExecutionContext(
                    instanceId: entity.InstanceId,
                    triggerMessageId: entity.TriggerMessageId,
                    correlationId: entity.CorrelationId,
                    descriptor: descriptor,
                    processedMessagesIds: entity.ProcessedMessages);

            var state = _serializer.Deserialize(entity.StateData, descriptor.SagaStateType);
            return CreateSagaContext((dynamic)state, entity, descriptor);
        }

        private static ISagaExecutionContext<TS> CreateSagaContext<TS>(TS state, SagaState entity, SagaDescriptor descriptor)
            => new SagaExecutionContext<TS>(
                   instanceId: entity.InstanceId,
                   triggerMessageId: entity.TriggerMessageId,
                   correlationId: entity.CorrelationId,
                   descriptor: descriptor,
                   state: state,
                   processedMessagesIds: entity.ProcessedMessages);

        public ValueTask<string> LockAsync(ISagaExecutionContext state, CancellationToken cancellationToken = default)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            return LockAsyncCore(state, cancellationToken);
        }

        private async ValueTask<string> LockAsyncCore(ISagaExecutionContext state, CancellationToken cancellationToken)
        {
            var entity = await _dbContext.SagaStates
                .FirstOrDefaultAsync(e => e.InstanceId == state.InstanceId, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                entity = new SagaState()
                {
                    CorrelationId = state.CorrelationId,
                    InstanceId = state.InstanceId,
                    IsCompleted = state.IsCompleted,
                    SagaType = state.Descriptor.SagaType.AssemblyQualifiedName,
                    SagaStateType = state.Descriptor.SagaStateType?.AssemblyQualifiedName,
                    TriggerMessageId = state.TriggerMessageId,                    
                };
                _dbContext.SagaStates.Add(entity);
            }
            else
            {
                if (entity.LockId is not null &&
                    entity.LockTime is not null &&
                    entity.LockTime > DateTimeOffset.UtcNow - _options.LockMaxDuration)
                    throw new LockException($"saga state '{state.InstanceId}' is already locked");               
            }

            entity.LockTime = DateTimeOffset.UtcNow;
            entity.LockId = Guid.NewGuid().ToString();

            await _dbContext.SaveChangesAsync(cancellationToken)
                            .ConfigureAwait(false);

            return entity.LockId;
        }

        public ValueTask SaveAsync(ISagaExecutionContext state, string lockId, CancellationToken cancellationToken = default)
        {
            if (state == null) 
                throw new ArgumentNullException(nameof(state));

            return SaveAsyncCore(state, lockId, cancellationToken);
        }

        private async ValueTask SaveAsyncCore(ISagaExecutionContext state, string lockId, CancellationToken cancellationToken)
        {
            var entity = await _dbContext.SagaStates
                 .FirstOrDefaultAsync(e => e.InstanceId == state.InstanceId, cancellationToken)
                 .ConfigureAwait(false);

            if (entity is null)
                throw new ArgumentException($"saga state '{state.InstanceId}' not found");

            if (entity.LockId != lockId)
                throw new LockException($"unable to release Saga State '{state.InstanceId}' with lock id {lockId}");

            entity.LockTime = null;
            entity.LockId = null;

            if (state.GetType().IsGenericType)
                SetStateData((dynamic)state, entity);

            await _dbContext.SaveChangesAsync(cancellationToken)
                        .ConfigureAwait(false);
        }

        private void SetStateData<TS>(ISagaExecutionContext<TS> state, SagaState entity)
        {
            entity.StateData = _serializer.Serialize(state.State);
        }
    }
}
