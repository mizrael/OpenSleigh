using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace OpenSleigh.Core.BackgroundServices
{
    public class SubscribersBackgroundService : BackgroundService
    {
        private readonly IEnumerable<ISubscriber> _subscribers;

        public SubscribersBackgroundService(IEnumerable<ISubscriber> subscribers)
        {
            _subscribers = subscribers ?? throw new ArgumentNullException(nameof(subscribers));
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            foreach (var subscriber in _subscribers)
                await subscriber.StartAsync(cancellationToken);

            await base.StartAsync(cancellationToken);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            foreach (var subscriber in _subscribers)
                await subscriber.StopAsync(cancellationToken);

            await base.StopAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
        }
        
    }

}
