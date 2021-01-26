using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core.Persistence
{
    public interface IOutboxRepository
    {
        Task<IEnumerable<IMessage>> ReadMessagesToProcess(CancellationToken cancellationToken = default);
        Task ReleaseAsync(IMessage message, Guid lockId, CancellationToken cancellationToken = default);
        Task AppendAsync(IMessage message, CancellationToken cancellationToken = default);
        Task CleanProcessedAsync(CancellationToken cancellationToken = default);
        Task<Guid> LockAsync(IMessage message, CancellationToken cancellationToken = default);
    }
}