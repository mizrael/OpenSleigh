using OpenSleigh.Persistence.SQL.Tests.Fixtures;

namespace OpenSleigh.Persistence.SQL.Tests.Integration.PostgreSQL;

public class PostgreSQLOutboxRepositoryTests :
    SqlOutboxRepositoryTests,
    IClassFixture<PostgreSQLDbFixture>
{
    public PostgreSQLOutboxRepositoryTests(PostgreSQLDbFixture fixture) : base(fixture)
    {
    }
}
