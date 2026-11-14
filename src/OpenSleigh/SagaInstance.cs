using OpenSleigh.Outbox;
using OpenSleigh.Transport;
using System.Collections.Concurrent;

namespace OpenSleigh;

public record SagaInstance : ISagaInstance 
{
    private readonly Dictionary<string, ProcessedMessage> _processedMessages = new();

    // created by the first message that is published: an instance is built for every message, and most publish nothing
    private ConcurrentQueue<MessageEnvelope>? _outbox;

    public SagaInstance(
        string instanceId, 
        string triggerMessageId, 
        string correlationId,
        SagaDescriptor descriptor,
        IEnumerable<ProcessedMessage>? processedMessages = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId, nameof(instanceId));
        ArgumentException.ThrowIfNullOrWhiteSpace(triggerMessageId, nameof(triggerMessageId));
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId, nameof(correlationId));

        InstanceId = instanceId;
        TriggerMessageId = triggerMessageId;
        CorrelationId = correlationId;
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));      
        
        if(processedMessages is not null)
            foreach(var msg in processedMessages)
                _processedMessages.Add(msg.MessageId, msg);
    }

    public bool CanProcess<TM>(IMessageContext<TM> messageContext) 
        where TM : IMessage
    {
        if (IsCompleted)
            return false;

        if (this.CorrelationId != messageContext.CorrelationId)
            return false;

        if (_processedMessages.ContainsKey(messageContext.MessageId))
            return false;

        var messageType = messageContext.Message.GetType();
        var isInitiator = this.Descriptor.InitiatorTypes.Contains(messageType);
        if (isInitiator)
            return true;

        return true;
    }
       
    public void SetAsProcessed<TM>(IMessageContext<TM> messageContext) where TM : IMessage
    {
        if(_processedMessages.ContainsKey(messageContext.MessageId))
            throw new InvalidOperationException($"Message with id {messageContext.MessageId} has already been processed.");

        _processedMessages.Add(messageContext.MessageId, ProcessedMessage.Create(messageContext));
    }

    public void MarkAsCompleted()
        => this.IsCompleted = true;

    public async ValueTask LockAsync(
        ISagaStateRepository sagaStateRepository,
        CancellationToken cancellationToken) 
    {
        this.LockId = await sagaStateRepository.LockAsync(this, cancellationToken)
                                              .ConfigureAwait(false);
    }

    public void Publish(MessageEnvelope message)
    {
        ArgumentNullException.ThrowIfNull(message);

        LazyInitializer.EnsureInitialized(ref _outbox).Enqueue(message);
    }

    public void ClearOutbox()
        => _outbox?.Clear();

    public SagaMessageFailure? Failure { get; private set; }

    public void RecordFailure(SagaMessageFailure? failure)
        => Failure = failure;

    public void HandLockTo(ISagaInstance other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (other is not SagaInstance target || target.InstanceId != InstanceId)
            throw new ArgumentException($"the lock of saga '{InstanceId}' can only be handed to another instance of it.", nameof(other));

        target.LockId = LockId;
        LockId = string.Empty;
    }

    public async ValueTask ProcessAsync<TM>(
        IMessageHandlerManager messageHandlerManager,
        IMessageContext<TM> messageContext,
        ISagaExecutionService sagaExecutionService,
        CancellationToken cancellationToken = default) where TM : IMessage
    {
        ArgumentNullException.ThrowIfNull(messageHandlerManager);
        ArgumentNullException.ThrowIfNull(messageContext);
        ArgumentNullException.ThrowIfNull(sagaExecutionService);

        try
        {
            await messageHandlerManager.ProcessAsync(this, messageContext, cancellationToken)
                                       .ConfigureAwait(false);
        }
        catch (SagaException failure)
        {
            // the handler failed. The saga is still locked: whatever is decided for the message is stored under its lock
            await sagaExecutionService.HandleFailureAsync(this, messageContext, messageHandlerManager, failure, cancellationToken)
                                      .ConfigureAwait(false);
            return;
        }
        catch
        {
            await TryAbandonAsync(sagaExecutionService).ConfigureAwait(false);
            throw;
        }

        try
        {
            await CommitAsProcessedAsync(messageContext, sagaExecutionService, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await TryAbandonAsync(sagaExecutionService).ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask CommitAsProcessedAsync<TM>(
        IMessageContext<TM> messageContext,
        ISagaExecutionService sagaExecutionService,
        CancellationToken cancellationToken) where TM : IMessage
    {
        this.SetAsProcessed(messageContext);

        try
        {
            await sagaExecutionService.CommitAsync(this, cancellationToken)
                                      .ConfigureAwait(false);
        }
        catch
        {
            // the messages were not stored: the message must not be released as processed, or a redelivery would
            // be ignored and they would never go out
            _processedMessages.Remove(messageContext.MessageId);
            ClearOutbox();
            throw;
        }

        // to be done after Commit, as it will be checked when releasing the state
        this.LockId = string.Empty;
    }

    // nothing of what the handler did is stored: the saga stays as it was before the message
    private async ValueTask TryAbandonAsync(ISagaExecutionService sagaExecutionService)
    {
        if (string.IsNullOrEmpty(this.LockId))
            return;

        ClearOutbox();

        try
        {
            await sagaExecutionService.AbandonAsync(this, CancellationToken.None)
                                      .ConfigureAwait(false);
        }
        catch
        {
            // best effort: lock will expire via LockMaxDuration for SQL/Mongo
        }

        this.LockId = string.Empty;
    }

    public string TriggerMessageId { get; }
    
    public string CorrelationId { get; }

    public string InstanceId { get; }

    public SagaDescriptor Descriptor { get; }

    public bool IsCompleted { get; private set; }

    public string LockId { get; private set; }

    public IReadOnlyCollection<ProcessedMessage> ProcessedMessages => _processedMessages.Values;
    public IReadOnlyCollection<MessageEnvelope> Outbox => _outbox ?? NoMessages;

    private static readonly IReadOnlyCollection<MessageEnvelope> NoMessages = Array.Empty<MessageEnvelope>();
}

public record SagaInstance<TS> : SagaInstance, ISagaInstance<TS>
{
    public SagaInstance(
        string instanceId, 
        string triggerMessageId, 
        string correlationId,
        SagaDescriptor descriptor, 
        TS state,
        IEnumerable<ProcessedMessage>? processedMessages = null) : base(
            instanceId: instanceId, 
            triggerMessageId: triggerMessageId,
            correlationId: correlationId,
            descriptor: descriptor,
            processedMessages: processedMessages)
    {
        this.State = state;
    }

    public TS State { get; }    
}