using OpenSleigh.DependencyInjection;
using OpenSleigh.Persistence.SQL.Tests.Fixtures;
using OpenSleigh.Transport.Kafka.Tests.Fixtures;

namespace OpenSleigh.E2ETests.SqlKafka;

public class SqlKafkaSimpleSagaScenario : 
    SimpleSagaScenario,
    IClassFixture<SqlServerDbFixture>,
    IClassFixture<KafkaFixture>
{
    private readonly KafkaFixture _kafkaFixture;        
    private readonly DbFixture _dbFixture;
    private readonly string _exchangeName;
    
    public SqlKafkaSimpleSagaScenario(SqlServerDbFixture dbFixture, KafkaFixture kafkaFixture)
    {
        _dbFixture = dbFixture;
        _kafkaFixture = kafkaFixture;
        _exchangeName = "SQLKafkaSimpleSagaScenario-" +
#if NET9_0_OR_GREATER
            Guid.CreateVersion7().ToString("N");
#else
            Guid.NewGuid().ToString("N");
#endif
    }

    protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
        => SqlKafkaScenarioUtils.ConfigureTransportAndPersistence(cfg, _dbFixture, _kafkaFixture, _exchangeName);
}
