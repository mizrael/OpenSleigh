using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace OpenSleigh.Core.BackgroundServices
{
    public class SubscribersBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private static readonly Type SubscriberRawType = typeof(ISubscriber);
        
        public SubscribersBackgroundService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();

            var subscribers = scope.ServiceProvider.GetServices<ISubscriber>();
            var tasks = subscribers.Select(s => s.StartAsync(stoppingToken));

            await Task.WhenAll(tasks.ToArray());
        }
    }
}
