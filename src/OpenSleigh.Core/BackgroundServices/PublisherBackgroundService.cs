using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace OpenSleigh.Core.BackgroundServices
{
    public class PublisherBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public PublisherBackgroundService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IMessagePublisherService>();
            await service.StartAsync(stoppingToken);
        }
    }

    public interface IMessagePublisherService
    {
        Task StartAsync(CancellationToken cancellationToken = default);
    }
}