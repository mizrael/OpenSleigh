using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core.Persistence
{
    //TODO: implement in-memory
    public interface IOutboxRepository
    {
        Task<IEnumerable<IMessage>> ReadMessagesToProcess(CancellationToken cancellationToken = default);
        Task MarkAsSentAsync(IMessage message, CancellationToken cancellationToken = default);
        Task AppendAsync(IMessage message, ITransaction transaction = null, CancellationToken cancellationToken = default);
    }
}