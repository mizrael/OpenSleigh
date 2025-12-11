using OpenSleigh.Outbox;
using OpenSleigh.Transport;

namespace OpenSleigh;

public class SagaExecutionService : ISagaExecutionService
{
    private readonly ISagaInstanceFactory _sagaExecCtxFactory;
    private readonly ISagaStateRepository _sagaStateRepository;
    private readonly IOutboxRepository _outboxRepository;

    public SagaExecutionService(
        ISagaInstanceFactory sagaExecCtxFactory,
        ISagaStateRepository sagaStateRepository,
        IOutboxRepository outboxRepository)
    {
        _sagaExecCtxFactory = sagaExecCtxFactory ?? throw new ArgumentNullException(nameof(sagaExecCtxFactory));
        _sagaStateRepository = sagaStateRepository ?? throw new ArgumentNullException(nameof(sagaStateRepository));
        _outboxRepository = outboxRepository ?? throw new ArgumentNullException(nameof(outboxRepository));
    }

    public async ValueTask<ISagaInstance> BeginProcessingAsync<TM>(
        IMessageContext<TM> messageContext,
        SagaDescriptor descriptor,
        CancellationToken cancellationToken = default)
        where TM : IMessage
    {
        var sagaInstance = await ResolveInstanceAsync(messageContext, descriptor, cancellationToken).ConfigureAwait(false);

        if (!sagaInstance.CanProcess(messageContext))
            return NoOpSagaInstance.Create(messageContext, descriptor);

        await sagaInstance.LockAsync(_sagaStateRepository, cancellationToken)
                          .ConfigureAwait(false);

        return sagaInstance;
    }

    public async ValueTask CommitAsync(
        ISagaInstance context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // TODO: transaction

        if (context.Outbox.Any())
        {
            await _outboxRepository.AppendAsync(context.Outbox, cancellationToken)
                                   .ConfigureAwait(false);
            context.ClearOutbox();
        }

        await _sagaStateRepository.ReleaseAsync(context, cancellationToken)
                                  .ConfigureAwait(false);
    }

    private async Task<ISagaInstance> ResolveInstanceAsync<TM>(IMessageContext<TM> messageContext, SagaDescriptor descriptor, CancellationToken cancellationToken) where TM : IMessage
    {
        var messageType = messageContext.Message.GetType();

        // we need to check if the state is already in the repository
        // even if the message is the initiator, as it might be a replay
        var sagaInstance = await _sagaStateRepository.FindAsync(descriptor, messageContext, cancellationToken);
        if (sagaInstance is null)
        {
            var isInitiator = descriptor.InitiatorTypes.Contains(messageType);
            if (isInitiator)
                sagaInstance = _sagaExecCtxFactory.Create(descriptor, messageContext);
        }

        return sagaInstance ?? throw new ApplicationException($"unable to locate state for Saga '{descriptor.SagaType}'.");
    }
}