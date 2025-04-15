using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Persistence.PostgreSQL;
using OpenSleigh.Transport.RabbitMQ;
using OpenSleigh.Samples.ECommerce.ShippingService.Sagas;
using OpenSleigh.DependencyInjection;

namespace OpenSleigh.Samples.ECommerce.ShippingService;

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
                        hostName: rabbitSection["HostName"],
                        vhost: rabbitSection["VHost"],
                        userName: rabbitSection["UserName"],
                        password: rabbitSection["Password"]);

                    var sqlConnStr = hostContext.Configuration.GetConnectionString("sql");
                    var sqlConfig = new SqlConfiguration(sqlConnStr);

                    cfg.UseRabbitMQTransport(rabbitCfg)
                        .UsePostgreSqlPersistence(sqlConfig)
                        .AddSaga<ShippingSaga, ShippingSagaState>();
                });
        });
}
