using OpenSleigh.Core.Persistence;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Persistence.Cosmos
{
    public class CosmosTransactionManager : ITransactionManager
    {
        // TODO: as of today, Cosmos has no support for multi-document transactions across collections.
        // https://docs.microsoft.com/en-ca/azure/cosmos-db/mongodb-feature-support-40#transactions
        public Task<ITransaction> StartTransactionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(NullTransaction.Instance);
    }
}