using OpenSleigh.Core.Exceptions;
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core.ExceptionPolicies;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Persistence;

namespace OpenSleigh.Core
{
    public class SagaRunner<TS, TD> : ISagaRunner<TS, TD>
        where TS : Saga<TD>
        where TD : SagaState
    {
        private readonly ISagaStateService<TS, TD> _sagaStateService;
        private readonly ISagaFactory<TS, TD> _sagaFactory;
        private readonly ITransactionManager _transactionManager;
        private readonly ILogger<SagaRunner<TS, TD>> _logger;
        private readonly ISagaPolicyFactory<TS> _policyFactory;

        public SagaRunner(ISagaFactory<TS, TD> sagaFactory,
                          ISagaStateService<TS, TD> sagaStateService, 
                          ITransactionManager transactionManager,
                          ISagaPolicyFactory<TS> policyFactory,
                          ILogger<SagaRunner<TS, TD>> logger)
        {
            _sagaFactory = sagaFactory ?? throw new ArgumentNullException(nameof(sagaFactory));
            _sagaStateService = sagaStateService ?? throw new ArgumentNullException(nameof(sagaStateService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _policyFactory = policyFactory ?? throw new ArgumentNullException(nameof(policyFactory));
            _transactionManager = transactionManager ?? throw new ArgumentNullException(nameof(transactionManager));
        }

        public async Task RunAsync<TM>(IMessageContext<TM> messageContext, CancellationToken cancellationToken = default)
            where TM : IMessage
        {
            var (state, lockId) = await GetStateAsync(messageContext, cancellationToken);

            if (state.IsCompleted())
            {
                _logger.LogWarning($"Stopped processing message '{messageContext.Message.Id}', Saga '{state.Id}' was already marked as completed");
                return;
            }
            
            if (state.CheckWasProcessed(messageContext.Message))
            {
                _logger.LogWarning($"message '{messageContext.Message.Id}' was already processed by saga '{state.Id}'");
                return;
            }

            var saga = _sagaFactory.Create(state);
            if (null == saga)
                throw new SagaException($"unable to create Saga of type '{typeof(TS).FullName}'");

            if (saga is not IHandleMessage<TM> handler)
                throw new ConsumerNotFoundException(typeof(TM));
            
            var transaction = await _transactionManager.StartTransactionAsync(cancellationToken);
            try
            {
                var policy = _policyFactory.Create<TM>();
                if (policy is null)
                    await handler.HandleAsync(messageContext, cancellationToken);
                else 
                    await policy.WrapAsync(() => handler.HandleAsync(messageContext, cancellationToken));

                state.SetAsProcessed(messageContext.Message);

                await _sagaStateService.SaveAsync(state, lockId, cancellationToken);

                await transaction.CommitAsync(cancellationToken);
            }
            catch(Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                
                if (saga is not ICompensateMessage<TM> compensatingHandler)
                    throw;

                await ExecuteCompensationAsync(compensatingHandler, messageContext, ex, state, lockId, cancellationToken);
            }
        }

        private async Task ExecuteCompensationAsync<TM>(ICompensateMessage<TM> compensatingHandler,
            IMessageContext<TM> messageContext,
            Exception exception,
            TD state,
            Guid lockId,
            CancellationToken cancellationToken) where TM : IMessage
        {
            _logger.LogWarning(exception, $"something went wrong when processing saga '{messageContext.Message.CorrelationId}' : {exception.Message}. executing compensation ...");
            
            var compensatingTransaction = await _transactionManager.StartTransactionAsync(cancellationToken);
            
            var compensationContext = DefaultCompensationContext<TM>.Build(messageContext, exception);
            
            try
            {
                await compensatingHandler.CompensateAsync(compensationContext, cancellationToken);

                state.SetAsProcessed(messageContext.Message);
                await _sagaStateService.SaveAsync(state, lockId, cancellationToken);

                await compensatingTransaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await compensatingTransaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        private async Task<(TD state, Guid lockId)> GetStateAsync<TM>(IMessageContext<TM> messageContext, CancellationToken cancellationToken)
            where TM : IMessage
        {
            var policy = Policy.Retry(builder =>
            {
                builder.WithDelay(i => TimeSpan.FromSeconds(i))
                    .Handle<LockException>()
                    .WithMaxRetries(10)
                    .OnException(ctx =>
                    {
                        _logger.LogWarning(
                            $"unable to lock state for saga '{messageContext.Message.CorrelationId}': '{ctx.Exception.Message}'. Retrying...");
                    });
            });
            return await policy.WrapAsync(() => _sagaStateService.GetAsync(messageContext, cancellationToken));
        }
    }
}