using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Persistence;
using OpenSleigh.Core.Utils;

namespace OpenSleigh.Persistence.SQL
{
    public class SqlOutboxRepository : IOutboxRepository
    {
        private readonly ISagaDbContext _dbContext;
        private readonly ISerializer _serializer;
        
        public SqlOutboxRepository(ISagaDbContext dbContext, ISerializer serializer)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
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
            if (message == null)
                throw new ArgumentNullException(nameof(message));
            
            throw new NotImplementedException();
        }

        public Task CleanProcessedAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public async Task<Guid> LockAsync(IMessage message, CancellationToken cancellationToken = default)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            var transaction = await _dbContext.StartTransactionAsync(cancellationToken);
            try
            {

                var entity = await _dbContext.OutboxMessages.FirstOrDefaultAsync(e => e.Id == message.Id,
                                                                    cancellationToken: cancellationToken)
                                                                    .ConfigureAwait(false);
                if (entity is null)
                    throw new ArgumentException($"message '{message.Id}' not found");

                var lockId = Guid.NewGuid();
                
                throw new NotImplementedException();

                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
    }
}
