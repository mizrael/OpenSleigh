using System;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core
{
    public interface ISagaStateService<TS, TD>
        where TS : Saga<TD>
        where TD : SagaState
    {
        Task<(TD state, Guid lockId)> GetAsync<TM>(IMessageContext<TM> messageContext,
                              CancellationToken cancellationToken = default) where TM : IMessage;

        Task SaveAsync(TD state, Guid lockId, CancellationToken cancellationToken = default);
    }
}