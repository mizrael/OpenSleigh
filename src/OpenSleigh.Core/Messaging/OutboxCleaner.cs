using System;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core.Persistence;

namespace OpenSleigh.Core.Messaging
{
    public class OutboxCleaner : IOutboxCleaner
    {
        private readonly IOutboxRepository _outboxRepository;
        
        public OutboxCleaner(IOutboxRepository outboxRepository)
        {
            _outboxRepository = outboxRepository ?? throw new ArgumentNullException(nameof(outboxRepository));
        }

        public Task RunCleanupAsync(CancellationToken cancellationToken = default) =>
            _outboxRepository.CleanProcessedAsync(cancellationToken);
    }
}