using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using OpenSleigh.Core.Messaging;

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
            {
                await Task.Factory.StartNew(async () =>
                {
                    var tasks = _subscribers.Select(s => s.StartAsync(cancellationToken)).ToArray();
                    await Task.WhenAll(tasks);
                },
                cancellationToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Current);
            }

            await base.StartAsync(cancellationToken);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            if (!_systemInfo.PublishOnly)
                await Task.WhenAll(_subscribers.Select(s => s.StopAsync(cancellationToken)));
            
            await base.StopAsync(cancellationToken);
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
            => Task.CompletedTask;
    }
}
