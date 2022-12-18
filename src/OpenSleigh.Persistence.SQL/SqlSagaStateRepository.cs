using Microsoft.EntityFrameworkCore;
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
            var entity = await _dbContext.SagaStates
                .AsNoTracking()
                .FirstOrDefaultAsync(e =>
                    e.CorrelationId == correlationId && 
                    e.SagaType == descriptor.SagaType.FullName &&
                    ((descriptor.SagaStateType == null && e.SagaStateType == null) || 
                    (descriptor.SagaStateType != null && e.SagaStateType == descriptor.SagaStateType.FullName)),
                    cancellationToken)
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

        private static ISagaExecutionContext<TS> CreateSagaContext<TS>(TS state, SagaState entity, SagaDescriptor descriptor)
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
                    SagaType = state.Descriptor.SagaType.FullName,
                    SagaStateType = state.Descriptor.SagaStateType?.FullName,
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

        public ValueTask ReleaseAsync(ISagaExecutionContext state, string lockId, CancellationToken cancellationToken = default)
        {
            if (state == null) 
                throw new ArgumentNullException(nameof(state));

            return ReleaseAsyncCore(state, lockId, cancellationToken);
        }

        private async ValueTask ReleaseAsyncCore(ISagaExecutionContext state, string lockId, CancellationToken cancellationToken)
        {
            var entity = await _dbContext.SagaStates
                 .FirstOrDefaultAsync(e => e.InstanceId == state.InstanceId, cancellationToken)
                 .ConfigureAwait(false);

            if (entity is null)
                throw new ArgumentException($"saga state '{state.InstanceId}' not found");

            if (entity.LockId != lockId)
                throw new LockException($"unable to release Saga State '{state.InstanceId}' with lock id '{lockId}'");

            entity.LockTime = null;
            entity.LockId = null;

            entity.IsCompleted = state.IsCompleted;
            
            entity.ProcessedMessages.Clear();

            foreach (var msg in state.ProcessedMessages)
                entity.ProcessedMessages.Add(new SagaProcessedMessage()
                {
                    InstanceId = state.InstanceId,
                    MessageId = msg.MessageId,
                    When = msg.When,
                    SagaState = entity
                });
                        
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
