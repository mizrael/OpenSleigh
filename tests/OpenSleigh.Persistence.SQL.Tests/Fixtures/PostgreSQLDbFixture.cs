namespace OpenSleigh.Persistence.SQL.Tests.Fixtures
{
    public class PostgreSQLDbFixture : DbFixture
    {
        public PostgreSQLDbFixture() : base("postgre")
        {
        }

        protected override DbContextOptionsBuilder<SagaDbContext> CreateOptionsBuilder(string connectionString)
        => new DbContextOptionsBuilder<SagaDbContext>()
                .UseNpgsql(connectionString)
                .EnableSensitiveDataLogging();
    }
}
