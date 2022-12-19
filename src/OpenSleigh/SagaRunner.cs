using Microsoft.Extensions.Logging;
using OpenSleigh.Transport;
using OpenSleigh.Outbox;

namespace OpenSleigh
{
    internal class SagaRunner : ISagaRunner
    {
        private readonly ISagaExecutionContextFactory _sagaStateFactory;
        private readonly ISagaStateRepository _sagaStateRepository;
        private readonly IOutboxRepository _outboxRepository;
        private readonly IMessageHandlerFactory _messageHandlerFactory;
        private readonly ILogger<SagaRunner> _logger;

        public SagaRunner(
            ISagaExecutionContextFactory sagaStateFactory,
            ISagaStateRepository sagaStateRepository,
            IMessageHandlerFactory messageHandlerFactory,
            IOutboxRepository outboxRepository,
            ILogger<SagaRunner> logger)
        {
            _sagaStateFactory = sagaStateFactory ?? throw new ArgumentNullException(nameof(sagaStateFactory));
            _sagaStateRepository = sagaStateRepository ?? throw new ArgumentNullException(nameof(sagaStateRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _messageHandlerFactory = messageHandlerFactory ?? throw new ArgumentNullException(nameof(messageHandlerFactory));
            _outboxRepository = outboxRepository ?? throw new ArgumentNullException(nameof(outboxRepository));
        }

        public async ValueTask ProcessAsync<TM>(
            IMessageContext<TM> messageContext,
            SagaDescriptor descriptor,
            CancellationToken cancellationToken = default)
            where TM : IMessage
        {
            var executionContext = await BuildExecutionContextAsync(messageContext, descriptor, cancellationToken).ConfigureAwait(false);

            if (!executionContext.CanProcess(messageContext))
                return;

            _logger.LogInformation(
                "Saga '{SagaType}/{InstanceId}' is processing message '{MessageId}'...",
                descriptor.SagaType,
                executionContext.InstanceId,
                messageContext.Id);

            IHandleMessage<TM> handler = _messageHandlerFactory.Create<TM>(descriptor.SagaType, executionContext);

            //TODO: create transaction
            //TODO: pass transaction to the handler factory

            string lockId = await _sagaStateRepository.LockAsync(executionContext, cancellationToken)
                                                      .ConfigureAwait(false);

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

            executionContext.SetAsProcessed(messageContext);

            await _sagaStateRepository.ReleaseAsync(executionContext, lockId, cancellationToken)
                                    .ConfigureAwait(false);

            _logger.LogInformation(
                "Saga '{SagaType}/{InstanceId}' has completed processing message '{MessageId}'.",
                descriptor.SagaType,
                executionContext.InstanceId,
                messageContext.Id);
        }

        private async Task<ISagaExecutionContext> BuildExecutionContextAsync<TM>(IMessageContext<TM> messageContext, SagaDescriptor descriptor, CancellationToken cancellationToken) where TM : IMessage
        {
            var messageType = messageContext.Message.GetType();

            ISagaExecutionContext? executionContext;
            var isInitiator = descriptor.InitiatorType == messageType;
            if (isInitiator)
            {
                executionContext = _sagaStateFactory.CreateState(descriptor, messageContext);
            }
            else
            {
                executionContext = await _sagaStateRepository.FindAsync(descriptor, messageContext.CorrelationId, cancellationToken);
                if (executionContext is null)
                    throw new ApplicationException($"unable to locate state for Saga '{descriptor.SagaType}'.");
            }

            return executionContext;
        }
    }
}