using OpenSleigh.DependencyInjection;
using OpenSleigh.Persistence.SQL.Tests.Fixtures;
using OpenSleigh.Transport.RabbitMQ.Tests.Fixtures;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.SqlRabbit;

public class SqlRabbitMultipleSagasSameMessagesScenario : 
    MultipleSagasSameMessagesScenario,
    IClassFixture<PostgreSQLDbFixture>,
    IClassFixture<RabbitFixture>
{
    private readonly RabbitFixture _rabbitFixture;
    private readonly PostgreSQLDbFixture _dbFixture;
    private readonly string _exchangeName;

    public SqlRabbitMultipleSagasSameMessagesScenario(PostgreSQLDbFixture dbFixture, RabbitFixture rabbitFixture, ITestOutputHelper console) : base(console)
    {
        _dbFixture = dbFixture;
        _rabbitFixture = rabbitFixture;
        _exchangeName = "SqlRabbitMultipleSagasSameMessagesScenario-" + Guid.NewGuid().ToString("N");
    }

    protected override void ConfigureTransportAndPersistence(IBusConfigurator cfg)
        => SqlRabbitScenarioUtils.ConfigureTransportAndPersistence(cfg, _dbFixture, _rabbitFixture, _exchangeName);
}