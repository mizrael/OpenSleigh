using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core.BackgroundServices
{
    public class OutboxBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly OutboxProcessorOptions _options;
        private readonly ILogger<OutboxBackgroundService> _logger;
        private readonly SystemInfo _systemInfo;

        public OutboxBackgroundService(IServiceScopeFactory scopeFactory,
            OutboxProcessorOptions options,
            ILogger<OutboxBackgroundService> logger, 
            SystemInfo systemInfo)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _systemInfo = systemInfo;
            _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation($"Outbox Background Service is starting on client '{_systemInfo.ClientId}' ...");

            await ProcessMessagesAsync(stoppingToken);
        }

        private async Task ProcessMessagesAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var service = scope.ServiceProvider.GetRequiredService<IOutboxProcessor>();
                    await service.ProcessPendingMessagesAsync(stoppingToken)
                        .ConfigureAwait(false);
                }

                await Task.Delay(_options.Interval, stoppingToken)
                    .ConfigureAwait(false);
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation($"Outbox Background Service is stopping on client '{_systemInfo.ClientId}' .");

            await base.StopAsync(cancellationToken);
        }
    }
}