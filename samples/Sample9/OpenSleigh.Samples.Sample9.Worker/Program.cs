using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Persistence.Mongo;
using OpenSleigh.Samples.Sample9.Common.Messages;
using OpenSleigh.Samples.Sample9.Worker.Sagas;
using OpenSleigh.Transport.Kafka;

namespace OpenSleigh.Samples.Sample9.Worker
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
                        var mongoSection = hostContext.Configuration.GetSection("Mongo");
                        var mongoCfg = new MongoConfiguration(mongoSection["ConnectionString"],
                            mongoSection["DbName"],
                            MongoSagaStateRepositoryOptions.Default,
                            MongoOutboxRepositoryOptions.Default);

                        var kafkaConnStr = hostContext.Configuration.GetSection("ConnectionStrings")["Kafka"];
                        var kafkaCfg = new KafkaConfiguration(kafkaConnStr);

                        cfg.UseKafkaTransport(kafkaCfg)
                            .UseMongoPersistence(mongoCfg);

                        cfg.AddSaga<SimpleSaga, SimpleSagaState>()
                            .UseStateFactory<StartSimpleSaga>(msg => new SimpleSagaState(msg.CorrelationId))
                            .UseKafkaTransport();

                        cfg.AddSaga<ParentSaga, ParentSagaState>()
                            .UseStateFactory<StartParentSaga>(msg => new ParentSagaState(msg.CorrelationId))
                            .UseKafkaTransport();

                        cfg.AddSaga<ChildSaga, ChildSagaState>()
                            .UseStateFactory<StartChildSaga>(msg => new ChildSagaState(msg.CorrelationId))
                            .UseKafkaTransport();
                    });
            });
    }
}
