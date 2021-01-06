using System;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core.Persistence;

namespace OpenSleigh.Core.Messaging
{
    public record OutboxCleanerOptions(TimeSpan Interval)
    {
        public static readonly OutboxCleanerOptions Default = new OutboxCleanerOptions(TimeSpan.FromSeconds(5));
    }
    
    public class OutboxCleaner : IOutboxCleaner
    {
        private readonly IOutboxRepository _outboxRepository;
        private readonly OutboxCleanerOptions _options;

        public OutboxCleaner(IOutboxRepository outboxRepository, OutboxCleanerOptions options)
        {
            _outboxRepository = outboxRepository ?? throw new ArgumentNullException(nameof(outboxRepository));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await RunCleanup(cancellationToken);
                await Task.Delay(_options.Interval, cancellationToken);
            }
        }

        private Task RunCleanup(CancellationToken cancellationToken) =>
            _outboxRepository.CleanProcessedAsync(cancellationToken);
    }
}