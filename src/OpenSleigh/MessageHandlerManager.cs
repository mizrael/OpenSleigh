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

            // what the handler published before it failed must not go out: only what the rollback publishes does
            executionContext.ClearOutbox();

            await RollbackAsync(handler, executionContext, messageContext, ex, cancellationToken).ConfigureAwait(false);

            throw new SagaException(FailureMessage(executionContext, messageContext, ex), ex) { RolledBack = true };
        }
    }

    private async ValueTask RollbackAsync<TM>(
        IHandleMessage<TM> handler,
        ISagaInstance executionContext,
        IMessageContext<TM> messageContext,
        Exception handlerFailure,
        CancellationToken cancellationToken) where TM : IMessage
    {
        try
        {
            await handler.RollbackAsync(messageContext, cancellationToken)
                         .ConfigureAwait(false);
        }
        // a rollback that was interrupted because the application is stopping has not failed either:
        // nothing of it is stored and the message is delivered again
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            executionContext.ClearOutbox();
            throw;
        }
        catch (Exception rollbackFailure)
        {
            _logger.LogError(
                rollbackFailure,
                "an error has occurred while rolling back message '{MessageId}' from Saga '{SagaType}/{InstanceId}' : {Error}",
                messageContext.MessageId,
                executionContext.Descriptor.SagaType,
                executionContext.InstanceId,
                rollbackFailure.Message
            );

            // a rollback that stopped half way is not stored in part
            executionContext.ClearOutbox();

            throw new SagaException(
                FailureMessage(executionContext, messageContext, handlerFailure),
                new AggregateException(handlerFailure, rollbackFailure));
        }
    }

    private static string FailureMessage<TM>(ISagaInstance executionContext, IMessageContext<TM> messageContext, Exception failure)
        where TM : IMessage
        => $"an error has occurred while processing message '{messageContext.MessageId}' from Saga '{executionContext.Descriptor.SagaType}/{executionContext.InstanceId}' : {failure.Message}";
}