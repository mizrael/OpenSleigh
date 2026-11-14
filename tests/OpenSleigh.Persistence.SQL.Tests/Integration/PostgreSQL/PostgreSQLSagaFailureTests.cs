using OpenSleigh.Persistence.SQL.Tests.Fixtures;

namespace OpenSleigh.Persistence.SQL.Tests.Integration.PostgreSQL;

public class PostgreSQLSagaFailureTests :
    SqlSagaFailureTests,
    IClassFixture<PostgreSQLDbFixture>
{
    public PostgreSQLSagaFailureTests(PostgreSQLDbFixture fixture) : base(fixture)
    {
    }
}
