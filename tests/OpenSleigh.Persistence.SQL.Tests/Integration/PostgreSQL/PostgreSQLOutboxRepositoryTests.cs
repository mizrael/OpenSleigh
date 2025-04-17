using OpenSleigh.Persistence.SQL.Tests.Fixtures;

namespace OpenSleigh.Persistence.SQL.Tests.Integration.PostgreSQL;

public class PostgreSQLOutboxRepositoryTests :
    SqlOutboxRepositoryTests,
    IClassFixture<PostgreSQLDbFixture>
{
    public PostgreSQLOutboxRepositoryTests(PostgreSQLDbFixture fixture) : base(fixture)
    {
    }

    protected override SqlOutboxRepository CreateSut(SagaDbContext db)
    {
        DuplicateKeyDetector duplicateKeyDetector = Persistence.PostgreSQL.SqlBusConfiguratorExtensions.IsDuplicateKeyException;
        return CreateSut(db, duplicateKeyDetector);
    }
}
