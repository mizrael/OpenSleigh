using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Persistence.PostgreSQL;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Samples.Sample11.Messages;
using OpenSleigh.Transport.Kafka;

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        services.AddLogging(cfg =>
        {
            cfg.AddConsole();
        })
        .AddOpenSleigh(cfg =>
        {
            var sqlConnStr = hostContext.Configuration.GetConnectionString("sql");
            var sqlConfig = new SqlConfiguration(sqlConnStr);

            var kafkaConnStr = hostContext.Configuration.GetConnectionString("Kafka");
            var kafkaCfg = new KafkaConfiguration(kafkaConnStr);

            cfg.UseKafkaTransport(kafkaCfg)
                .UsePostgreSqlPersistence(sqlConfig);

            cfg.AddMessageHandlers<SayHello>(new[] { typeof(Program).Assembly })
                .UseKafkaTransport();
        });
    });
var host = builder.Build();

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