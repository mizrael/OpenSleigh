using OpenSleigh.Persistence.SQL.Tests.Fixtures;
using OpenSleigh.Persistence.SQLServer;
using OpenSleigh.Utils;

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

        var typeResolver = new TypeResolver();
        typeResolver.Register(typeof(FakeMessage));

        var sut = new MSSqlOutboxRepository(db, typeResolver, SqlOutboxRepositoryOptions.Default, new JsonSerializer(), duplicateKeyDetector);
        return sut;
    }
}
