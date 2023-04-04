using OpenSleigh.Transport;

namespace OpenSleigh
{
    public class SagaExecutionService : ISagaExecutionService
    {
        private readonly ISagaExecutionContextFactory _sagaExecCtxFactory;
        private readonly ISagaStateRepository _sagaStateRepository;

        public SagaExecutionService(ISagaExecutionContextFactory sagaExecCtxFactory, ISagaStateRepository sagaStateRepository)
        {
            _sagaExecCtxFactory = sagaExecCtxFactory ?? throw new ArgumentNullException(nameof(sagaExecCtxFactory));
            _sagaStateRepository = sagaStateRepository ?? throw new ArgumentNullException(nameof(sagaStateRepository));
        }
        
        public async ValueTask<(ISagaExecutionContext? context, string? lockId)> BeginAsync<TM>(
            IMessageContext<TM> messageContext, 
            SagaDescriptor descriptor, 
            CancellationToken cancellationToken = default) 
            where TM : IMessage
        {
            var executionContext = await BuildExecutionContextAsync(messageContext, descriptor, cancellationToken).ConfigureAwait(false);

            if (!executionContext.CanProcess(messageContext))
                return (null, null);

            string lockId = await _sagaStateRepository.LockAsync(executionContext, cancellationToken)
                                                      .ConfigureAwait(false);

            return (executionContext, lockId);
        }

        public async ValueTask CommitAsync<TM>(
            ISagaExecutionContext context,
            IMessageContext<TM> messageContext,
            string lockId, 
            CancellationToken cancellationToken = default) where TM : IMessage
        {
            context.SetAsProcessed(messageContext);

            await _sagaStateRepository.ReleaseAsync(context, lockId, cancellationToken)
                                    .ConfigureAwait(false);
        }

        private async Task<ISagaExecutionContext> BuildExecutionContextAsync<TM>(IMessageContext<TM> messageContext, SagaDescriptor descriptor, CancellationToken cancellationToken) where TM : IMessage
        {
            var messageType = messageContext.Message.GetType();

            ISagaExecutionContext? executionContext;
            var isInitiator = descriptor.InitiatorType == messageType;
            if (isInitiator)
            {
                executionContext = _sagaExecCtxFactory.CreateState(descriptor, messageContext);
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