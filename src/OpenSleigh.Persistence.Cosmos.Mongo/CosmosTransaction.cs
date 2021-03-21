using OpenSleigh.Core.Persistence;
using MongoDB.Driver;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Persistence.Cosmos.Mongo
{
    public sealed class CosmosTransaction : ITransaction, IDisposable
    {
        public readonly IClientSessionHandle Session;

        public CosmosTransaction(IClientSessionHandle session)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            await Session.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            await Session.AbortTransactionAsync(cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
            Session?.Dispose();
        }
    }
}