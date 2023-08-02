using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenSleigh.Transport;

namespace OpenSleigh
{
    public class SubscribersBackgroundService : BackgroundService
    {
        private readonly IEnumerable<ISubscriber> _subscribers;
        private readonly ISystemInfo _systemInfo;
        private readonly ILogger<SubscribersBackgroundService> _logger;

        public SubscribersBackgroundService(
            IEnumerable<ISubscriber> subscribers,
            ISystemInfo systemInfo,
            ILogger<SubscribersBackgroundService> logger)
        {
            _subscribers = subscribers ?? throw new ArgumentNullException(nameof(subscribers));
            _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            if (!_systemInfo.PublishOnly)
            {
                _logger.LogInformation($"stopping subscribers on client '{_systemInfo.ClientId}' ...");
                await Task.WhenAll(_subscribers.Select(s => s.StopAsync(cancellationToken).AsTask()));
            }

            await base.StopAsync(cancellationToken);
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_systemInfo.PublishOnly)
            {
                _logger.LogInformation($"client '{_systemInfo.ClientId}' is set to Publish Only.");
                return Task.CompletedTask;
            }

            _logger.LogInformation($"starting subscribers on client '{_systemInfo.ClientGroup}/{_systemInfo.ClientId}' ...");

            var tasks = _subscribers.Select(s => s.StartAsync(stoppingToken).AsTask());
            var combinedTask = Task.WhenAll(tasks);
            return combinedTask;
        }
    }
}