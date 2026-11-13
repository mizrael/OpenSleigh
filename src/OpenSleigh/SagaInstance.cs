using OpenSleigh.Outbox;
using OpenSleigh.Transport;
using System.Collections.Concurrent;

namespace OpenSleigh;

public record SagaInstance : ISagaInstance 
{
    private readonly Dictionary<string, ProcessedMessage> _processedMessages = new();
    private readonly ConcurrentQueue<MessageEnvelope> _outbox = new();

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

        _outbox.Enqueue(message);
    }

    public void ClearOutbox()
        => _outbox.Clear();

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

            this.SetAsProcessed(messageContext);

            await sagaExecutionService.CommitAsync(this, cancellationToken)
                                      .ConfigureAwait(false);

            // to be done after Commit, as it will be checked when releasing the state
            this.LockId = string.Empty;
        }
        catch (SagaException failure) when (failure.RolledBack && !string.IsNullOrEmpty(this.LockId))
        {
            await CommitRollbackAsync(messageContext, sagaExecutionService, cancellationToken).ConfigureAwait(false);
            throw;
        }
        catch
        {
            await TryReleaseAsync(sagaExecutionService).ConfigureAwait(false);
            throw;
        }
    }

    // the handler failed and its rollback ran: what the rollback published is stored with the release, like the messages
    // of a handler that succeeded. The message is recorded as processed, so that a redelivery does not roll back twice
    private async ValueTask CommitRollbackAsync<TM>(
        IMessageContext<TM> messageContext,
        ISagaExecutionService sagaExecutionService,
        CancellationToken cancellationToken) where TM : IMessage
    {
        try
        {
            this.SetAsProcessed(messageContext);

            await sagaExecutionService.CommitAsync(this, cancellationToken)
                                      .ConfigureAwait(false);

            this.LockId = string.Empty;
        }
        catch
        {
            // nothing of the rollback was stored: the message is reported with this failure, not with the handler's
            await TryReleaseAsync(sagaExecutionService).ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask TryReleaseAsync(ISagaExecutionService sagaExecutionService)
    {
        if (string.IsNullOrEmpty(this.LockId))
            return;

        try
        {
            await sagaExecutionService.ReleaseAsync(this, CancellationToken.None)
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
    public IReadOnlyCollection<MessageEnvelope> Outbox => _outbox;
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