using OpenSleigh.Persistence.SQL.Tests.Fixtures;

namespace OpenSleigh.Persistence.SQL.Tests.Integration.SQLServer
{
    public class SqlServerSagaStateRepositoryTests :
        SqlSagaStateRepositoryTests,
        IClassFixture<SqlServerDbFixture>
    {
        public SqlServerSagaStateRepositoryTests(SqlServerDbFixture fixture) : base(fixture)
        {
        }
    }
}
