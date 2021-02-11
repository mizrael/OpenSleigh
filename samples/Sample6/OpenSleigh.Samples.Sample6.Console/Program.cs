using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Persistence.InMemory;
using OpenSleigh.Samples.Sample6.Console.Sagas;
using OpenSleigh.Transport.AzureServiceBus;

namespace OpenSleigh.Samples.Sample6.Console
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var hostBuilder = CreateHostBuilder(args);
            var host = hostBuilder.Build();
            
            //TODO: update readme
            await host.SetupInfrastructureAsync();

            using var scope = host.Services.CreateScope();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            var message = new StartSaga(Guid.NewGuid(), Guid.NewGuid());

            await Task.WhenAll(new[]
            {
                host.RunAsync(),
                bus.PublishAsync(message)
            });
        }

        static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration(builder => { builder.AddUserSecrets<Program>(); })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddLogging(cfg => { cfg.AddConsole(); })
                        .AddOpenSleigh(cfg =>
                        {
                            var connStr = hostContext.Configuration.GetConnectionString("AzureServiceBus");
                            var azureSBCfg = new AzureServiceBusConfiguration(connStr);
                            cfg.UseAzureServiceBusTransport(azureSBCfg)
                                .UseInMemoryPersistence();

                            cfg.AddSaga<MySaga, MySagaState>()
                                .UseStateFactory<StartSaga>(msg => new MySagaState(msg.CorrelationId))
                                .UseAzureServiceBusTransport();
                        });
                });
    }
}
