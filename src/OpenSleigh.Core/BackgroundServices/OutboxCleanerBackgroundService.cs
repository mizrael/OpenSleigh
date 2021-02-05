using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core.BackgroundServices
{
    public class OutboxCleanerBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly OutboxCleanerOptions _options;
        
        public OutboxCleanerBackgroundService(IServiceScopeFactory scopeFactory, OutboxCleanerOptions options)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Run(async () =>
            {
                while (true)
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var service = scope.ServiceProvider.GetRequiredService<IOutboxCleaner>();
                        await service.RunCleanupAsync(stoppingToken)
                            .ConfigureAwait(false);
                    }

                    await Task.Delay(_options.Interval, stoppingToken)
                        .ConfigureAwait(false);
                }
            }, stoppingToken);
        }
    }
}