using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Persistence.PostgreSQL;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Samples.ECommerce.NotificationsService.EventHandlers;
using OpenSleigh.Transport.RabbitMQ;

static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                config.AddJsonFile("appsettings.json", true, true);
            })
            .ConfigureLogging((ctx, cfg) =>
            {
                cfg.AddConfiguration(ctx.Configuration.GetSection("Logging"));
            })
            .ConfigureServices((hostContext, services) =>
            {
                services.AddOpenSleigh(cfg =>
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
                       .UsePostgreSqlPersistence(sqlConfig);
                    cfg.AddSaga<NotificationsHandlers>();
                });
            });

var hostBuilder = CreateHostBuilder(args);
var host = hostBuilder.Build();
await host.RunAsync();