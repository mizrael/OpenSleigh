using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Persistence.PostgreSQL;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Persistence.SQL.Tests.Fixtures;
using OpenSleigh.Transport.RabbitMQ;
using OpenSleigh.Transport.RabbitMQ.Tests.Fixtures;

namespace OpenSleigh.E2ETests.SqlRabbit;

internal static class SqlRabbitScenarioUtils
{
    public static void ConfigureTransportAndPersistence(
        IBusConfigurator cfg,
        DbFixture dbFixture,
        RabbitFixture rabbitFixture,
        string exchangeName)
    {
        var (_, connStr) = dbFixture.CreateDbContext(exchangeName);
        var sqlCfg = new SqlConfiguration(connStr);

        QueueReferencesCreator creator = messageType =>
        {
            var queueName = $"{exchangeName}.{messageType.Name}.workers";
            var dlExchangeName = exchangeName + ".dead";
            var dlQueueName = $"{dlExchangeName}.{messageType.Name}.workers";
            return new QueueReferences(exchangeName, queueName, dlExchangeName, dlQueueName);
        };
        cfg.Services.AddSingleton(creator);

        cfg.UsePostgreSqlPersistence(sqlCfg)
            .UseRabbitMQTransport(rabbitFixture.RabbitConfiguration);
    }
}