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
        var isInitiator = this.Descriptor.InitiatorType == messageType;
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
        CancellationToken cancellationToken) where TM : IMessage
    {
        await messageHandlerManager.ProcessAsync(this, messageContext, cancellationToken)
                                   .ConfigureAwait(false);

        this.SetAsProcessed(messageContext);

        await sagaExecutionService.CommitAsync(this, cancellationToken)
                                  .ConfigureAwait(false);

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

public record SagaExecutionContext<TS> : SagaInstance, ISagaExecutionContext<TS>
{
    public SagaExecutionContext(
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