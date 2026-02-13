using OpenSleigh.Persistence.SQL.Entities;

namespace OpenSleigh.Persistence.SQL;

internal interface ISagaContextFactory
{
    ISagaInstance Create(object state, SagaState entity, SagaDescriptor descriptor);
}

internal sealed class SagaContextFactory<TS> : ISagaContextFactory
{
    public ISagaInstance Create(object state, SagaState entity, SagaDescriptor descriptor)
        => new SagaInstance<TS>(
            instanceId: entity.InstanceId,
            triggerMessageId: entity.TriggerMessageId,
            correlationId: entity.CorrelationId,
            descriptor: descriptor,
            state: (TS)state,
            processedMessages: entity.ProcessedMessages.Select(e => new ProcessedMessage
            {
                MessageId = e.MessageId,
                When = e.When
            }));
}
