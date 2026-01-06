using OpenSleigh.Persistence.PostgreSQL;
using OpenSleigh.Persistence.SQL.Tests.Fixtures;
using OpenSleigh.Utils;

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
        var typeResolver = new TypeResolver();
        typeResolver.Register(typeof(FakeMessage));

        var sut = new PostgreSQLOutboxRepository(SqlOutboxRepositoryOptions.Default, db, typeResolver, new JsonSerializer(), duplicateKeyDetector);
        return sut;
    }
}
