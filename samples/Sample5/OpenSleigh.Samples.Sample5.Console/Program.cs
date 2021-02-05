using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Persistence.InMemory;
using OpenSleigh.Samples.Sample5.Console.Sagas;

namespace OpenSleigh.Samples.Sample5.Console
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var hostBuilder = CreateHostBuilder(args);
            var host = hostBuilder.Build();

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
                            .UseInMemoryTransport()
                            .UseRetryPolicy<StartSaga>(builder =>
                            {
                                builder.WithMaxRetries(5)
                                    .Handle<ApplicationException>()
                                    .OnException(ctx =>
                                    {
                                        System.Console.WriteLine(
                                            $"tentative #{ctx.ExecutionIndex} failed: {ctx.Exception.Message}");
                                    });
                            });
                    });
            });
    }
}
