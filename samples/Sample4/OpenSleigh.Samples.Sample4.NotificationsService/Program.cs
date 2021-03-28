using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Samples.Sample4.Common;
using OpenSleigh.Transport.RabbitMQ;

namespace OpenSleigh.Samples.Sample4.NotificationsService
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

                        cfg.UseRabbitMQTransport(rabbitCfg)
                            .UseSqlPersistence(sqlConfig);

                        cfg.AddMessageHandlers(new[] {typeof(Program).Assembly});
                    });
            });
    }
}
