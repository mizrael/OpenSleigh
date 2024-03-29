﻿using OpenSleigh.Outbox;
using OpenSleigh.Transport;

namespace OpenSleigh
{
    public class SagaExecutionService : ISagaExecutionService
    {
        private readonly ISagaExecutionContextFactory _sagaExecCtxFactory;
        private readonly ISagaStateRepository _sagaStateRepository;
        private readonly IOutboxRepository _outboxRepository;

        public SagaExecutionService(
            ISagaExecutionContextFactory sagaExecCtxFactory, 
            ISagaStateRepository sagaStateRepository, 
            IOutboxRepository outboxRepository)
        {
            _sagaExecCtxFactory = sagaExecCtxFactory ?? throw new ArgumentNullException(nameof(sagaExecCtxFactory));
            _sagaStateRepository = sagaStateRepository ?? throw new ArgumentNullException(nameof(sagaStateRepository));
            _outboxRepository = outboxRepository ?? throw new ArgumentNullException(nameof(outboxRepository));
        }

        public async ValueTask<ISagaExecutionContext> StartExecutionContextAsync<TM>(
            IMessageContext<TM> messageContext, 
            SagaDescriptor descriptor, 
            CancellationToken cancellationToken = default) 
            where TM : IMessage
        {
            var executionContext = await BuildExecutionContextAsync(messageContext, descriptor, cancellationToken).ConfigureAwait(false);

            if (!executionContext.CanProcess(messageContext))
                return NoOpSagaExecutionContext.Create(messageContext, descriptor);

            await executionContext.LockAsync(_sagaStateRepository, cancellationToken)
                                  .ConfigureAwait(false);

            return executionContext;
        }

        public async ValueTask CommitAsync(
            ISagaExecutionContext context,            
            CancellationToken cancellationToken = default) 
        {
            // TODO: transaction

            await _sagaStateRepository.ReleaseAsync(context, cancellationToken)
                                     .ConfigureAwait(false);
            
            await _outboxRepository.AppendAsync(context.Outbox, cancellationToken)
                                   .ConfigureAwait(false);

            context.ClearOutbox();
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