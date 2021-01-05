using OpenSleigh.Core.Persistence;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Persistence.InMemory
{
    internal class InMemoryUnitOfWork : IUnitOfWork
    {
        public Task<ITransaction> StartTransactionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new InMemoryTransaction() as ITransaction);
    }
}