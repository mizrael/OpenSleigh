using OpenSleigh.Core.Exceptions;
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace OpenSleigh.Core
{
    public class SagaRunner<TS, TD> : ISagaRunner<TS, TD>
        where TS : Saga<TD>
        where TD : SagaState
    {
        private readonly ISagaStateService<TS, TD> _sagaStateService;
        private readonly ISagaFactory<TS, TD> _sagaFactory;
        private readonly ILogger<SagaRunner<TS, TD>> _logger;
        
        public SagaRunner(ISagaFactory<TS, TD> sagaFactory,
                          ISagaStateService<TS, TD> sagaStateService,
                          ILogger<SagaRunner<TS, TD>> logger)
        {
            _sagaFactory = sagaFactory ?? throw new ArgumentNullException(nameof(sagaFactory));
            _sagaStateService = sagaStateService ?? throw new ArgumentNullException(nameof(sagaStateService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task RunAsync<TM>(IMessageContext<TM> messageContext, CancellationToken cancellationToken)
            where TM : IMessage
        {
            var done = false;
            var random = new Random();
            TD state = null;
            Guid lockId = Guid.Empty;
            while (!done) // TODO: better retry policy (max retries? Polly?)
            {
                try
                {
                    (state, lockId) = await _sagaStateService.GetAsync(messageContext, cancellationToken);

                    done = true;
                }
                catch (LockException)
                {
                    //TODO: logging
                    await Task.Delay(TimeSpan.FromMilliseconds(random.Next(1, 10)), cancellationToken).ConfigureAwait(false);
                }
            }

            try
            {
                if (state.CheckWasProcessed(messageContext.Message))
                {
                    _logger.LogWarning($"message '{messageContext.Message.Id}' was already processed by saga '{state.Id}'");
                    return;
                }

                var saga = _sagaFactory.Create(state);
                if (null == saga)
                    throw new SagaNotFoundException($"unable to create Saga of type '{typeof(TS).FullName}'");

                if (saga is not IHandleMessage<TM> handler)
                    throw new ConsumerNotFoundException(typeof(TM));

                //TODO: add configurable retry policy
                await handler.HandleAsync(messageContext, cancellationToken);

                state.SetAsProcessed(messageContext.Message);
            }
            finally
            {
                await _sagaStateService.SaveAsync(state, lockId, cancellationToken);
            }
        }
    }
}