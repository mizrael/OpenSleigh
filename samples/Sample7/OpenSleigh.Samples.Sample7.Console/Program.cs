using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Persistence.InMemory;
using OpenSleigh.Samples.Sample7.Console.Sagas;

namespace OpenSleigh.Samples.Sample7.Console
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var hostBuilder = CreateHostBuilder(args);
            var host = hostBuilder.Build();

            using var scope = host.Services.CreateScope();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            var messages = new[]
            {
                new StartSaga(Guid.NewGuid(), Guid.NewGuid()),
                new StartSaga(Guid.NewGuid(), Guid.NewGuid(), true)
            };

            var tasks = messages
                .Select(m => bus.PublishAsync(m))
                .Union(new[] {host.RunAsync()})
                .ToArray();

            await Task.WhenAll(tasks);
        }

        static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
            .ConfigureServices((hostContext, services) =>
            {
                services.AddLogging(cfg =>
                    {
                        cfg.AddConsole();
                    })
                    .AddOpenSleigh(cfg =>
                    {
                        cfg.UseInMemoryTransport()
                            .UseInMemoryPersistence();
                        
                        cfg.AddSaga<MySaga, MySagaState>()
                            .UseStateFactory<StartSaga>(msg => new MySagaState(msg.CorrelationId))
                            .UseInMemoryTransport();
                    });
            });
    }
}
