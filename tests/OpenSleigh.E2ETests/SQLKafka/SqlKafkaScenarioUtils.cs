using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Persistence.SQL.Tests.Fixtures;
using OpenSleigh.Persistence.SQLServer;
using OpenSleigh.Transport.Kafka;
using OpenSleigh.Transport.Kafka.Tests.Fixtures;

namespace OpenSleigh.E2ETests.SqlKafka;

internal static class SqlKafkaScenarioUtils
{
    public static void ConfigureTransportAndPersistence(
        IBusConfigurator cfg,
        DbFixture dbFixture,
        KafkaFixture kafkaFixture,
        string exchangeName)
    {
        var (_, connStr) = dbFixture.CreateDbContext();
        var sqlCfg = new SqlConfiguration(connStr);

        QueueReferencesCreator creator = messageType =>
        {
            var topicName = messageType.Name.ToLower();
            return new QueueReferences(topicName, topicName + ".dead");
        };
        cfg.Services.AddSingleton(creator);

        var kafkaConfig = kafkaFixture.BuildKafkaConfiguration(exchangeName);

        cfg.UseSqlServerPersistence(sqlCfg)
            .UseKafkaTransport(kafkaConfig);
    }
}