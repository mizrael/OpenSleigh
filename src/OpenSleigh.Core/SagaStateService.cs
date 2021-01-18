using OpenSleigh.Core.Exceptions;
using OpenSleigh.Core.Persistence;
using System;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core
{
    public class SagaStateService<TS, TD> : ISagaStateService<TS, TD>
        where TS : Saga<TD>
        where TD : SagaState
    {
        private readonly ISagaStateFactory<TD> _sagaStateFactory;
        private readonly ISagaStateRepository _sagaStateRepository;

        public SagaStateService(ISagaStateFactory<TD> sagaStateFactory, ISagaStateRepository uow)
        {
            _sagaStateFactory = sagaStateFactory ?? throw new ArgumentNullException(nameof(sagaStateFactory));
            _sagaStateRepository = uow ?? throw new ArgumentNullException(nameof(uow));
        }

        public async Task<(TD state, Guid lockId)> GetAsync<TM>(IMessageContext<TM> messageContext,
            CancellationToken cancellationToken = default) where TM : IMessage
        {
            var correlationId = messageContext.Message.CorrelationId;
            
            var isStartMessage = (typeof(IStartedBy<TM>).IsAssignableFrom(typeof(TS)));
            TD initialState = null;
            if (isStartMessage)
                initialState = _sagaStateFactory.Create(messageContext.Message);
            
            var result = await _sagaStateRepository.LockAsync(correlationId, initialState, cancellationToken);

            if (null != result.state)
                return result;

            // if state is null, we're probably starting a new saga.
            // We have to check if the current message can
            // actually start the specified saga or not
            if (!isStartMessage)
                throw new MessageException($"Saga '{correlationId}' cannot be started by message '{typeof(TM).FullName}'");

            throw new StateCreationException(typeof(TD), correlationId);
        }

        public async Task SaveAsync(TD state, Guid lockId, ITransaction transaction = null, CancellationToken cancellationToken = default)
        {
            await _sagaStateRepository.ReleaseLockAsync(state, lockId, transaction, cancellationToken);
        }
    }
}