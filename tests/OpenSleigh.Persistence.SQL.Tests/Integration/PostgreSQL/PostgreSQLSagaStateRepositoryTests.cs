using OpenSleigh.Persistence.SQL.Tests.Fixtures;

namespace OpenSleigh.Persistence.SQL.Tests.Integration.PostgreSQL
{
    public class PostgreSQLSagaStateRepositoryTests :
        SqlSagaStateRepositoryTests,
        IClassFixture<PostgreSQLDbFixture>
    {
        public PostgreSQLSagaStateRepositoryTests(PostgreSQLDbFixture fixture) : base(fixture)
        {
        }
    }
}
