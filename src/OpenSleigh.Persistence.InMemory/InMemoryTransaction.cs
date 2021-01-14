using System.Diagnostics.CodeAnalysis;
using OpenSleigh.Core.Persistence;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Persistence.InMemory
{
    [ExcludeFromCodeCoverage]
    internal class InMemoryTransaction : ITransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}