using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Persistence.Mongo;
using OpenSleigh.Samples.Sample2.Worker.Sagas;
using OpenSleigh.Transport.RabbitMQ;
using System.Threading.Tasks;

namespace OpenSleigh.Samples.Sample2.Worker
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
                        var rabbitCfg = new RabbitConfiguration(
                            rabbitSection["HostName"],
                            rabbitSection["VirtualHost"],
                            rabbitSection["UserName"],
                            rabbitSection["Password"]);

                        var mongoSection = hostContext.Configuration.GetSection("Mongo");
                        var mongoCfg = new MongoConfiguration(mongoSection["ConnectionString"],
                            mongoSection["DbName"],
                            MongoSagaStateRepositoryOptions.Default,
                            MongoOutboxRepositoryOptions.Default);

                        cfg.UseRabbitMQTransport(rabbitCfg)
                           .UseMongoPersistence(mongoCfg);

                        cfg.AddSaga<SimpleSaga, SimpleSagaState>()
                           .AddSaga<ParentSaga, ParentSagaState>()
                           .AddSaga<ChildSaga, ChildSagaState>();
                    });
            });
    }
}
