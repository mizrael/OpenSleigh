using OpenSleigh.Persistence.SQLServer;

namespace OpenSleigh.Persistence.SQL.Tests.Fixtures;

public class SqlServerDbFixture : DbFixture
{
    public SqlServerDbFixture() : base("sql")
    {
    }

    protected override DbContextOptionsBuilder<SagaDbContext> CreateOptionsBuilder(string connectionString)
    => new DbContextOptionsBuilder<SagaDbContext>()
            .UseSqlServer(connectionString)
            .AddInterceptors(new QueryHintInterceptor()); // this is necessary to have the outbox working
}
