using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core.BackgroundServices
{
    public class SubscribersBackgroundService : BackgroundService
    {
        private readonly IEnumerable<ISubscriber> _subscribers;
        private readonly SystemInfo _systemInfo;
        private readonly ILogger<SubscribersBackgroundService> _logger;

        public SubscribersBackgroundService(IEnumerable<ISubscriber> subscribers, 
            SystemInfo systemInfo, 
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
                await Task.WhenAll(_subscribers.Select(s => s.StopAsync(cancellationToken)));
            }                

            await base.StopAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_systemInfo.PublishOnly)
            {
                _logger.LogInformation($"no subscribers on client '{_systemInfo.ClientId}'");
                return;
            }                

            _logger.LogInformation($"starting subscribers on client '{_systemInfo.ClientId}' ...");

            var tasks = _subscribers.Select(s => s.StartAsync(stoppingToken));
            await Task.WhenAll(tasks);
        }            
    }
}
