using OpenSleigh.Persistence.SQL.Tests.Fixtures;

namespace OpenSleigh.Persistence.SQL.Tests.Integration.SQLServer;

public class SqlServerOutboxRepositoryTests :
    SqlOutboxRepositoryTests,
    IClassFixture<SqlServerDbFixture>
{
    public SqlServerOutboxRepositoryTests(SqlServerDbFixture fixture) : base(fixture)
    {
    }

    protected override SqlOutboxRepository CreateSut(SagaDbContext db)
    {
        DuplicateKeyDetector duplicateKeyDetector = Persistence.SQLServer.SqlBusConfiguratorExtensions.IsDuplicateKeyException;
        return CreateSut(db, duplicateKeyDetector);
    }
}
