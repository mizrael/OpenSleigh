using OpenSleigh.Core.Exceptions;
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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
        private readonly IUnitOfWork _uow;
        private readonly ILogger<SagaRunner<TS, TD>> _logger;
        private static readonly Random _rand = new();
        
        public SagaRunner(ISagaFactory<TS, TD> sagaFactory,
                          ISagaStateService<TS, TD> sagaStateService, 
                          IUnitOfWork uow,
                          ILogger<SagaRunner<TS, TD>> logger)
        {
            _sagaFactory = sagaFactory ?? throw new ArgumentNullException(nameof(sagaFactory));
            _sagaStateService = sagaStateService ?? throw new ArgumentNullException(nameof(sagaStateService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
        }

        public async Task RunAsync<TM>(IMessageContext<TM> messageContext, CancellationToken cancellationToken)
            where TM : IMessage
        {
            var done = false;
            TD state = null;
            var lockId = Guid.Empty;
            while (!done) // TODO: better retry policy (max retries? Polly?)
            {
                try
                {
                    (state, lockId) = await _sagaStateService.GetAsync(messageContext, cancellationToken);

                    done = true;
                }
                catch (LockException ex)
                {
                    _logger.LogWarning($"unable to lock state for saga '{messageContext.Message.CorrelationId}': '{ex.Message}'. Retrying...");
                    await Task.Delay(TimeSpan.FromMilliseconds(_rand.Next(1, 10)), cancellationToken).ConfigureAwait(false);
                }
            }
            
            //TODO: if saga is marked as complete, stop processing
            
            if (state.CheckWasProcessed(messageContext.Message))
            {
                _logger.LogWarning($"message '{messageContext.Message.Id}' was already processed by saga '{state.Id}'");
                return;
            }

            var transaction = await _uow.StartTransactionAsync(cancellationToken);
            try
            {
                var saga = _sagaFactory.Create(state);
                if (null == saga)
                    throw new SagaNotFoundException($"unable to create Saga of type '{typeof(TS).FullName}'");

                if (saga is not IHandleMessage<TM> handler)
                    throw new ConsumerNotFoundException(typeof(TM));

                saga.Bus.SetTransaction(transaction);
                
                //TODO: add configurable retry policy
                await handler.HandleAsync(messageContext, cancellationToken);

                state.SetAsProcessed(messageContext.Message);

                await _sagaStateService.SaveAsync(state, lockId, transaction, cancellationToken);

                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
    
        }
    }
}