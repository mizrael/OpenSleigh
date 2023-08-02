using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Transport;
using OpenSleigh.Outbox;
using System.ComponentModel;

namespace OpenSleigh.E2ETests
{
    [Category("E2E")]
    [Trait("Category", "E2E")]
    public abstract class E2ETestsBase
    {
        protected async ValueTask<IHost> SetupHost(Action<HostBuilderContext, IServiceCollection> servicesBuilder)
        {
            var hostBuilder = CreateHostBuilder();
            
            hostBuilder.ConfigureServices(servicesBuilder);

            var host = hostBuilder.Build();

            //TODO:
            //  await host.SetupInfrastructureAsync();
            return host;
        }
        
        protected async Task RunScenarioAsync(int hostsCount,
            Action<HostBuilderContext, IServiceCollection> configureServices,
            Func<IMessageBus, Task> runner,
            CancellationTokenSource runningTokenSource)
        {
            var startupTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(60) * hostsCount);

            var createHostTasks = Enumerable.Range(1, hostsCount)
                .Select(async i =>
                {
                    var host = await SetupHost(configureServices);

                    await host.StartAsync(startupTokenSource.Token);
                    return host;
                }).ToArray();

            await Task.WhenAll(createHostTasks);

            if (startupTokenSource.IsCancellationRequested)
                throw new Exception("a timeout occurred during hosts initialization.");

            var producerHost = createHostTasks.First().Result;
            var scope = producerHost.Services.CreateScope();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

            await runner(bus);

            while (!runningTokenSource.IsCancellationRequested)
                await Task.Delay(10);

            foreach (var t in createHostTasks)
            {
                try
                {
                    t.Result.Dispose();
                }
                catch { }
            }
        }


        private IHostBuilder CreateHostBuilder() =>
            Host.CreateDefaultBuilder()
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddOpenSleigh(cfg =>
                    {
                        ConfigureTransportAndPersistence(cfg);

                        cfg.WithOutboxProcessorOptions(new OutboxProcessorOptions(TimeSpan.Zero));

                        RegisterSagas(cfg);
                    });
                });

        protected abstract void ConfigureTransportAndPersistence(IBusConfigurator cfg);
        protected abstract void RegisterSagas(IBusConfigurator cfg);
    }
}