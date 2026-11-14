using OpenSleigh.Outbox;
using OpenSleigh.Transport;

namespace OpenSleigh;

public class SagaExecutionService : ISagaExecutionService
{
    private readonly ISagaInstanceFactory _sagaExecCtxFactory;
    private readonly ISagaStateRepository _sagaStateRepository;
    private readonly IOutboxRepository _outboxRepository;
    private readonly IOutboxSignal? _outboxSignal;

    public SagaExecutionService(
        ISagaInstanceFactory sagaExecCtxFactory,
        ISagaStateRepository sagaStateRepository,
        IOutboxRepository outboxRepository,
        IOutboxSignal? outboxSignal = null)
    {
        _sagaExecCtxFactory = sagaExecCtxFactory ?? throw new ArgumentNullException(nameof(sagaExecCtxFactory));
        _sagaStateRepository = sagaStateRepository ?? throw new ArgumentNullException(nameof(sagaStateRepository));
        _outboxRepository = outboxRepository ?? throw new ArgumentNullException(nameof(outboxRepository));
        _outboxSignal = outboxSignal;
    }

    public async ValueTask<ISagaInstance> BeginProcessingAsync<TM>(
        IMessageContext<TM> messageContext,
        SagaDescriptor descriptor,
        CancellationToken cancellationToken = default)
        where TM : IMessage
    {
        ISagaInstance sagaInstance;
        
        try
        {
            sagaInstance = await BeginProcessingCore(messageContext, descriptor, cancellationToken).ConfigureAwait(false);
        }
        catch (OptimisticLockException ole)
        {
            try
            {
                sagaInstance = await BeginProcessingCore(messageContext, descriptor, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception retryException)
            {
                // Preserve both the original OptimisticLockException and the retry failure exception
                // This ensures debugging information is not lost in production
                throw new AggregateException(
                    "Failed to lock saga after optimistic lock conflict. Original exception and retry failure included.",
                    ole,
                    retryException);
            }
        }

        return sagaInstance;
    }

    private async Task<ISagaInstance> BeginProcessingCore<TM>(IMessageContext<TM> messageContext, SagaDescriptor descriptor, CancellationToken cancellationToken)
        where TM : IMessage
    {
        var sagaInstance = await ResolveInstanceAsync(messageContext, descriptor, cancellationToken).ConfigureAwait(false);

        if (!sagaInstance.CanProcess(messageContext))
            return NoOpSagaInstance.Create(messageContext, descriptor);

        await sagaInstance.LockAsync(_sagaStateRepository, cancellationToken)
                          .ConfigureAwait(false);

        // re-check after lock to close TOCTOU window: another thread may have
        // processed the same message between the initial check and lock acquisition
        if (!sagaInstance.CanProcess(messageContext))
        {
            await _sagaStateRepository.AbandonAsync(sagaInstance, cancellationToken)
                                      .ConfigureAwait(false);
            return NoOpSagaInstance.Create(messageContext, descriptor);
        }

        return sagaInstance;
    }

    public ValueTask ReleaseAsync(
        ISagaInstance context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return _sagaStateRepository.ReleaseAsync(context, cancellationToken);
    }

    public ValueTask AbandonAsync(
        ISagaInstance context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return _sagaStateRepository.AbandonAsync(context, cancellationToken);
    }

    public async ValueTask CommitAsync(
        ISagaInstance context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var hasNewMessages = context.Outbox.Count > 0;

        // looked up now: the outbox is empty once the messages are stored
        DateTimeOffset? earliestDueTime = hasNewMessages && _outboxSignal is not null ? EarliestDueTime(context.Outbox) : null;

        if (_sagaStateRepository is ISagaStateCommitter committer)
        {
            // messages and release are stored together: a relay cannot hand a message back to this saga
            // while it is still locked, or before the state it was published from is saved
            await committer.CommitAsync(context, cancellationToken)
                           .ConfigureAwait(false);
            context.ClearOutbox();
        }
        else
        {
            if (hasNewMessages)
            {
                await _outboxRepository.AppendAsync(context.Outbox, cancellationToken)
                                       .ConfigureAwait(false);
                context.ClearOutbox();
            }

            await _sagaStateRepository.ReleaseAsync(context, cancellationToken)
                                      .ConfigureAwait(false);
        }

        // a relay woken up for messages that are not due yet would read the outbox for nothing.
        // A short delay may have elapsed while the messages were stored
        if (earliestDueTime is { } dueTime && IsDue(dueTime))
            _outboxSignal?.NotifyPendingMessages();
    }

    private static readonly DateTimeOffset NoDelay = DateTimeOffset.MinValue;

    private static DateTimeOffset EarliestDueTime(IReadOnlyCollection<MessageEnvelope> messages)
    {
        var earliest = DateTimeOffset.MaxValue;

        foreach (var message in messages)
        {
            if (message.DeliverAt is not { } dueTime)
                return NoDelay;

            if (dueTime < earliest)
                earliest = dueTime;
        }

        return earliest;
    }

    private static bool IsDue(DateTimeOffset dueTime)
        => dueTime == NoDelay || dueTime <= DateTimeOffset.UtcNow;

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