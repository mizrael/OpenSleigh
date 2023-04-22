using OpenSleigh.Persistence.SQL.Tests.Fixtures;
using Xunit;

namespace OpenSleigh.Persistence.SQL.Tests.Integration.PostgreSQL
{
    public class PostgreSQLOutboxRepositoryTests :
        SqlOutboxRepositoryTests,
        IClassFixture<PostgreSQLDbFixture>
    {
        public PostgreSQLOutboxRepositoryTests(PostgreSQLDbFixture fixture) : base(fixture)
        {
        }
    }
}
