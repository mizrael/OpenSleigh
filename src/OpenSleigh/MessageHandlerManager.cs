using Microsoft.Extensions.Logging;
using OpenSleigh.Transport;
using OpenSleigh.Outbox;

namespace OpenSleigh
{
    public class MessageHandlerManager : IMessageHandlerManager
    {
        private readonly IOutboxRepository _outboxRepository;
        private readonly IMessageHandlerFactory _messageHandlerFactory;
        private readonly ILogger<MessageHandlerManager> _logger;

        public MessageHandlerManager(IOutboxRepository outboxRepository, IMessageHandlerFactory messageHandlerFactory, ILogger<MessageHandlerManager> logger)
        {
            _outboxRepository = outboxRepository ?? throw new ArgumentNullException(nameof(outboxRepository));
            _messageHandlerFactory = messageHandlerFactory ?? throw new ArgumentNullException(nameof(messageHandlerFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async ValueTask ProcessAsync<TM>(
            IMessageContext<TM> messageContext,            
            ISagaExecutionContext executionContext,
            CancellationToken cancellationToken) where TM : IMessage
        {
            IHandleMessage<TM> handler = _messageHandlerFactory.Create<TM>(executionContext);

            //TODO: create transaction
            //TODO: pass transaction to the handler factory            

            try
            {
                await handler.HandleAsync(messageContext, cancellationToken)
                            .ConfigureAwait(false);

                if (handler is IHasOutbox outbox)
                    await outbox.PersistOutboxAsync(_outboxRepository, cancellationToken)
                                .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "an error has occurred while processing message '{MessageId}' from Saga '{SagaType}/{InstanceId}' : {Error}",
                    messageContext.Id,
                    executionContext.Descriptor.SagaType,
                    executionContext.InstanceId,
                    ex.Message
                );

                await handler.RollbackAsync(messageContext, cancellationToken)
                             .ConfigureAwait(false);

                throw new SagaException(
                    $"an error has occurred while processing message '{messageContext.Id}' from Saga '{executionContext.Descriptor.SagaType}/{executionContext.InstanceId}' : {ex.Message}",
                    ex);
            }
        }
    }
}