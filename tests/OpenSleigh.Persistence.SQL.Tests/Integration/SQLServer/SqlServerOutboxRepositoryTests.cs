using OpenSleigh.Persistence.SQL.Tests.Fixtures;
using Xunit;

namespace OpenSleigh.Persistence.SQL.Tests.Integration.SQLServer
{
    public class SqlServerOutboxRepositoryTests :
        SqlOutboxRepositoryTests,
        IClassFixture<SqlServerDbFixture>
    {
        public SqlServerOutboxRepositoryTests(SqlServerDbFixture fixture) : base(fixture)
        {
        }
    }
}
