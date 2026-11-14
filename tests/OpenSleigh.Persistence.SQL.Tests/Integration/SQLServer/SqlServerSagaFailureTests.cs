using OpenSleigh.Persistence.SQL.Tests.Fixtures;

namespace OpenSleigh.Persistence.SQL.Tests.Integration.SQLServer;

public class SqlServerSagaFailureTests :
    SqlSagaFailureTests,
    IClassFixture<SqlServerDbFixture>
{
    public SqlServerSagaFailureTests(SqlServerDbFixture fixture) : base(fixture)
    {
    }
}
