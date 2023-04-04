using Microsoft.Extensions.Logging;
using OpenSleigh.Transport;
using OpenSleigh.Outbox;

namespace OpenSleigh
{
    public class MessageHandlerRunner : IMessageHandlerRunner
    {
        private readonly IOutboxRepository _outboxRepository;
        private readonly IMessageHandlerFactory _messageHandlerFactory;
        private readonly ILogger<MessageHandlerRunner> _logger;

        public MessageHandlerRunner(IOutboxRepository outboxRepository, IMessageHandlerFactory messageHandlerFactory, ILogger<MessageHandlerRunner> logger)
        {
            _outboxRepository = outboxRepository ?? throw new ArgumentNullException(nameof(outboxRepository));
            _messageHandlerFactory = messageHandlerFactory ?? throw new ArgumentNullException(nameof(messageHandlerFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task ProcessAsync<TM>(
            IMessageContext<TM> messageContext,
            SagaDescriptor descriptor,
            ISagaExecutionContext executionContext,
            CancellationToken cancellationToken) where TM : IMessage
        {
            IHandleMessage<TM> handler = _messageHandlerFactory.Create<TM>(descriptor.SagaType, executionContext);

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
                    descriptor.SagaType,
                    executionContext.InstanceId,
                    ex.Message
                );

                await handler.RollbackAsync(messageContext, cancellationToken)
                             .ConfigureAwait(false);

                throw new SagaException(
                    $"an error has occurred while processing message '{messageContext.Id}' from Saga '{descriptor.SagaType}/{executionContext.InstanceId}' : {ex.Message}",
                    ex);
            }
        }
    }
}