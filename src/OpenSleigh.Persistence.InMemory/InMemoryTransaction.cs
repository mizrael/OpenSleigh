using OpenSleigh.Core.Persistence;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Persistence.InMemory
{
    internal class InMemoryTransaction : ITransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}