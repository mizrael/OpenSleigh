using System;
using System.Threading;
using System.Threading.Tasks;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Persistence;

namespace OpenSleigh.Persistence.Mongo.Messaging
{
    public record MongoOutboxCleanerOptions(TimeSpan Interval)
    {
        public static MongoOutboxCleanerOptions Default = new MongoOutboxCleanerOptions(TimeSpan.FromSeconds(5));
    }
    
    public class MongoOutboxCleaner : IOutboxCleaner
    {
        private readonly IOutboxRepository _outboxRepository;
        private readonly MongoOutboxCleanerOptions _options;

        public MongoOutboxCleaner(IOutboxRepository outboxRepository, MongoOutboxCleanerOptions options)
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