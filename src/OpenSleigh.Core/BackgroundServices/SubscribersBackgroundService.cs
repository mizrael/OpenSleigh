using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace OpenSleigh.Core.BackgroundServices
{
    public class SubscribersBackgroundService : BackgroundService
    {
        private readonly IEnumerable<ISubscriber> _subscribers;
        private readonly SystemInfo _systemInfo;

        public SubscribersBackgroundService(IEnumerable<ISubscriber> subscribers, SystemInfo systemInfo)
        {
            _subscribers = subscribers ?? throw new ArgumentNullException(nameof(subscribers));
            _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            if (!_systemInfo.PublishOnly)
                Parallel.ForEach(_subscribers, async subscriber => await subscriber.StartAsync(cancellationToken));

            await base.StartAsync(cancellationToken);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            if (!_systemInfo.PublishOnly)
                Parallel.ForEach(_subscribers, async subscriber => await subscriber.StopAsync(cancellationToken));

            await base.StopAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
        }
    }
}
