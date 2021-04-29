using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Samples.Sample3.Common.Messages;
using OpenSleigh.Samples.Sample3.Worker.Sagas;
using OpenSleigh.Transport.RabbitMQ;

namespace OpenSleigh.Samples.Sample3.Worker
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var hostBuilder = CreateHostBuilder(args);
            var host = hostBuilder.Build();

            await host.RunAsync();
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
                        var rabbitSection = hostContext.Configuration.GetSection("Rabbit");
                        var rabbitCfg = new RabbitConfiguration(rabbitSection["HostName"], 
                            rabbitSection["UserName"],
                            rabbitSection["Password"]);

                        var sqlConnStr = hostContext.Configuration.GetConnectionString("sql");
                        var sqlConfig = new SqlConfiguration(sqlConnStr);
                        
                        cfg.UseRabbitMQTransport(rabbitCfg, builder =>
                            {
                                // using a custom naming policy allows us to define the names for exchanges and queues.
                                // this allows us to have a single exchange bound to multiple queues.
                                // messages will be routed using the queue name.
                                builder.UseMessageNamingPolicy<StartChildSaga>(() =>
                                    new QueueReferences("child", "child.start", "child.start", "child.dead", "child.dead.start"));
                                builder.UseMessageNamingPolicy<ProcessChildSaga>(() =>
                                    new QueueReferences("child", "child.process", "child.process", "child.dead", "child.dead.process"));
                            })
                            .UseSqlPersistence(sqlConfig);

                        cfg.AddSaga<SimpleSaga, SimpleSagaState>()
                            .UseStateFactory<StartSimpleSaga>(msg => new SimpleSagaState(msg.CorrelationId))
                            .UseRabbitMQTransport();

                        cfg.AddSaga<ParentSaga, ParentSagaState>()
                            .UseStateFactory<StartParentSaga>(msg => new ParentSagaState(msg.CorrelationId))
                            .UseRabbitMQTransport();

                        cfg.AddSaga<ChildSaga, ChildSagaState>()
                            .UseStateFactory<StartChildSaga>(msg => new ChildSagaState(msg.CorrelationId))
                            .UseRabbitMQTransport();
                    });
            });
    }
}
