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
        private readonly ITransactionManager _transactionManager;
        private readonly ILogger<SagaRunner<TS, TD>> _logger;
        private static readonly Random _rand = new();
        
        public SagaRunner(ISagaFactory<TS, TD> sagaFactory,
                          ISagaStateService<TS, TD> sagaStateService, 
                          ITransactionManager transactionManager,
                          ILogger<SagaRunner<TS, TD>> logger)
        {
            _sagaFactory = sagaFactory ?? throw new ArgumentNullException(nameof(sagaFactory));
            _sagaStateService = sagaStateService ?? throw new ArgumentNullException(nameof(sagaStateService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _transactionManager = transactionManager ?? throw new ArgumentNullException(nameof(transactionManager));
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

            var transaction = await _transactionManager.StartTransactionAsync(cancellationToken);
            try
            {
                var saga = _sagaFactory.Create(state);
                if (null == saga)
                    throw new SagaNotFoundException($"unable to create Saga of type '{typeof(TS).FullName}'");

                if (saga is not IHandleMessage<TM> handler)
                    throw new ConsumerNotFoundException(typeof(TM));

                //TODO: add configurable retry policy
                await handler.HandleAsync(messageContext, cancellationToken);

                state.SetAsProcessed(messageContext.Message);

                await _sagaStateService.SaveAsync(state, lockId, cancellationToken);

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