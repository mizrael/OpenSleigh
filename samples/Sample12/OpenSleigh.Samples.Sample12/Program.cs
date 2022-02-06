using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Persistence.InMemory;
using OpenSleigh.Samples.Sample12.Messages;
using OpenSleigh.Transport.AzureServiceBus;

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(( ctx, builder) =>
    {
        builder.AddJsonFile("appSettings.json", optional: false)
                .AddUserSecrets<Program>();
    })
    .ConfigureServices((hostContext, services) =>
    {
        services.AddLogging(cfg =>
        {
            cfg.AddConsole();
        })
        .AddOpenSleigh(cfg =>
        {
            var sbConnStr = hostContext.Configuration.GetConnectionString("ServiceBus");
            var sbConfig = new AzureServiceBusConfiguration(sbConnStr);
            cfg.UseInMemoryPersistence()
                .UseAzureServiceBusTransport(sbConfig);

            cfg.AddMessageHandlers<SayHello>(new[] { typeof(Program).Assembly })
                .UseAzureServiceBusTransport();
        });
    });
var host = builder.Build();

await host.SetupInfrastructureAsync();

var tokenSource = new CancellationTokenSource();
await Task.WhenAll(host.RunAsync(tokenSource.Token), RunAsync());

async Task RunAsync()
{
    var bus = host.Services.GetRequiredService<IMessageBus>();

    Console.BackgroundColor = ConsoleColor.Green;
    Console.ForegroundColor = ConsoleColor.White;
    Console.WriteLine("type 'exit' to quit");
    Console.ResetColor();

    var name = "";
    while (true)
    {
        Console.BackgroundColor = ConsoleColor.Green;
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write("Who do you want to greet? ");
        Console.ResetColor();

        name = Console.ReadLine();
        if (name == "exit")
            break;
        if (!string.IsNullOrWhiteSpace(name))
        {
            Console.BackgroundColor = ConsoleColor.Green;
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"sending a message to {name} ...");
            Console.ResetColor();

            await bus.PublishAsync(SayHello.Create(name));
        }            
    }

    tokenSource.Cancel();
}