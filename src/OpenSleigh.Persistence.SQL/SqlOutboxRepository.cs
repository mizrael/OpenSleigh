using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Persistence;

namespace OpenSleigh.Persistence.SQL
{
    public class SqlOutboxRepository : IOutboxRepository
    {
        private readonly ISagaDbContext _dbContext;

        public SqlOutboxRepository(ISagaDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public Task<IEnumerable<IMessage>> ReadMessagesToProcess(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task ReleaseAsync(IMessage message, Guid lockId, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task AppendAsync(IMessage message, ITransaction transaction = null, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task CleanProcessedAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<Guid> LockAsync(IMessage message, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}
