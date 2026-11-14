using Microsoft.Extensions.Logging;
using OpenSleigh.Transport;

namespace OpenSleigh;

public class MessageHandlerManager : IMessageHandlerManager
{
    private readonly IMessageHandlerFactory _messageHandlerFactory;
    private readonly ILogger<MessageHandlerManager> _logger;

    public MessageHandlerManager(IMessageHandlerFactory messageHandlerFactory, ILogger<MessageHandlerManager> logger)
    {
        _messageHandlerFactory = messageHandlerFactory ?? throw new ArgumentNullException(nameof(messageHandlerFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask ProcessAsync<TM>(
        ISagaInstance  executionContext,
        IMessageContext<TM> messageContext,
        CancellationToken cancellationToken = default) where TM : IMessage
    {
        IHandleMessage<TM> handler = _messageHandlerFactory.Create<TM>(executionContext);

        try
        {
            await handler.HandleAsync(messageContext, cancellationToken)
                        .ConfigureAwait(false);
        }
        // a handler that was interrupted because the application is stopping has not failed: the message is delivered again
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                ex,
                "an error has occurred while processing message '{MessageId}' from Saga '{SagaType}/{InstanceId}' : {Error}",
                messageContext.MessageId,
                executionContext.Descriptor.SagaType,
                executionContext.InstanceId,
                ex.Message
            );

            throw new SagaException(
                $"an error has occurred while processing message '{messageContext.MessageId}' from Saga '{executionContext.Descriptor.SagaType}/{executionContext.InstanceId}' : {ex.Message}",
                ex);
        }
    }

    public ValueTask CompensateAsync<TM>(
        ISagaInstance executionContext,
        IMessageContext<TM> messageContext,
        MessageFailure failure,
        CancellationToken cancellationToken = default) where TM : IMessage
    {
        if (_messageHandlerFactory.Create<TM>(executionContext) is not ICompensateMessage<TM> compensation)
            throw new InvalidOperationException(
                $"Saga '{executionContext.Descriptor.SagaType}' has no compensation handler for messages of type '{typeof(TM)}'");

        return compensation.CompensateAsync(messageContext, failure, cancellationToken);
    }
}