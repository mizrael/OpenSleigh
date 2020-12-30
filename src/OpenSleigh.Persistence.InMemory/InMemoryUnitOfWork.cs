using OpenSleigh.Core.Persistence;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Persistence.InMemory
{
    internal class InMemoryUnitOfWork : IUnitOfWork
    {
        public InMemoryUnitOfWork(ISagaStateRepository sagaStatesRepository)
        {
            SagaStatesRepository = sagaStatesRepository ?? throw new ArgumentNullException(nameof(sagaStatesRepository));
        }

        public ISagaStateRepository SagaStatesRepository { get; }

        public async Task<ITransaction> StartTransactionAsync(CancellationToken cancellationToken = default) => new InMemoryTransaction();
    }
}