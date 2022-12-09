using OpenSleigh.Samples.Sample1; 

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenSleigh.DependencyInjection;
using OpenSleigh.InMemory;
using OpenSleigh.Messaging;
using Microsoft.Extensions.Configuration;

static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                config.AddJsonFile("appsettings.json", true, true);
            })
            .ConfigureLogging((ctx, cfg) =>
            {
                cfg.AddConfiguration(ctx.Configuration.GetSection("Logging"))
                    //.AddConsole()
                    ;
                
            })
            .ConfigureServices((hostContext, services) =>
            {
                services.AddOpenSleigh(cfg =>
                {
                    cfg.UseInMemoryTransport()
                       .UseInMemoryPersistence()
                       .AddSaga<SagaWithoutState>()
                       .AddSaga<SagaWithState, MySagaState>()
                       ;
                });
            });

var hostBuilder = CreateHostBuilder(args);
var host = hostBuilder.Build();

using var scope = host.Services.CreateScope();
var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
var message = new StartSaga();

await Task.WhenAll(new[]
{
    host.RunAsync(),
    bus.PublishAsync(message).AsTask()
});