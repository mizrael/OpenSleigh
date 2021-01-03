using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Core.Persistence
{
    public class NullTransaction : ITransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}