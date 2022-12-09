using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OpenSleigh.Outbox
{
    public class OutboxBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly OutboxProcessorOptions _options;
        private readonly ILogger<OutboxBackgroundService> _logger;
        private readonly ISystemInfo _systemInfo;

        public OutboxBackgroundService(IServiceScopeFactory scopeFactory,
            OutboxProcessorOptions options,
            ILogger<OutboxBackgroundService> logger,
            ISystemInfo systemInfo)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "Outbox Background Service is starting on client '{ClientId}' ...",
                _systemInfo.ClientId);

            return Task.Run(async () => await ProcessMessagesAsync(stoppingToken), stoppingToken);
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
            _logger.LogInformation("Outbox Background Service is stopping on client '{ClientId}'.", _systemInfo.ClientId);

            await base.StopAsync(cancellationToken);
        }
    }
}