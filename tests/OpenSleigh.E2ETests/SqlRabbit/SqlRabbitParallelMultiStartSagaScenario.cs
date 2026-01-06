using OpenSleigh.DependencyInjection;
using OpenSleigh.Persistence.SQL.Tests.Fixtures;
using OpenSleigh.Transport.RabbitMQ.Tests.Fixtures;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.SqlRabbit;

public class SqlRabbitParallelMultiStartSagaScenario :
    ParallelMultiStartSagaScenario,
    IClassFixture<PostgreSQLDbFixture>,
    IClassFixture<RabbitFixture>
{
    private readonly RabbitFixture _rabbitFixture;
    private readonly DbFixture _dbFixture;
    private readonly string _exchangeName;

    public SqlRabbitParallelMultiStartSagaScenario(
        PostgreSQLDbFixture dbFixture,
        RabbitFixture rabbitFixture,
        ITestOutputHelper console
    ) : base(console)
    {
        _dbFixture = dbFixture;
        _rabbitFixture = rabbitFixture;
        _exchangeName = "SqlRabbitParallelMultiStartSagaScenario-" + Guid.NewGuid().ToString("N");
    }

    protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
        => SqlRabbitScenarioUtils.ConfigureTransportAndPersistence(cfg, _dbFixture, _rabbitFixture, _exchangeName);
}
