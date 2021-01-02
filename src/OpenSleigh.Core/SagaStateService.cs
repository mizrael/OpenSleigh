using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Exceptions;
using OpenSleigh.Core.Persistence;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Core
{
    public class SagaStateService<TS, TD> : ISagaStateService<TS, TD>
        where TS : Saga<TD>
        where TD : SagaState
    {
        private readonly ISagaStateFactory<TD> _sagaStateFactory;
        private readonly IUnitOfWork _uow;
        private readonly IMessageBus _bus;

        public SagaStateService(ISagaStateFactory<TD> sagaStateFactory, IUnitOfWork uow, IMessageBus publisher)
        {
            _sagaStateFactory = sagaStateFactory ?? throw new ArgumentNullException(nameof(sagaStateFactory));
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _bus = publisher ?? throw new ArgumentNullException(nameof(publisher));
        }

        public async Task<(TD state, Guid lockId)> GetAsync<TM>(IMessageContext<TM> messageContext,
            CancellationToken cancellationToken = default) where TM : IMessage
        {
            var correlationId = messageContext.Message.CorrelationId;

            var defaultState = _sagaStateFactory.Create(messageContext.Message);

            var result = await _uow.SagaStatesRepository.LockAsync(correlationId, defaultState, cancellationToken);

            if (null != result.state)
                return result;

            // if state is null, we're probably starting a new saga.
            // We have to check if the current message can
            // actually start the specified saga or not
            if (!typeof(IStartedBy<TM>).IsAssignableFrom(typeof(TS)))
                throw new StateCreationException(typeof(TD), $"saga cannot be started by message '{typeof(TM).FullName}'");

            throw new StateCreationException(typeof(TD), "unable to create saga state instance");
        }

        public async Task SaveAsync(TD state, Guid lockId, CancellationToken cancellationToken = default)
        {
            await _uow.SagaStatesRepository.UpdateAsync(state, lockId, true, cancellationToken);
        }
    }
}