using OpenSleigh.Core.Persistence;
using MongoDB.Driver;
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("OpenSleigh.Persistence.Mongo.Tests")]
namespace OpenSleigh.Persistence.Mongo
{
    internal sealed class MongoTransaction : ITransaction, IDisposable
    {
        public readonly IClientSessionHandle Session;

        public MongoTransaction(IClientSessionHandle session)
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