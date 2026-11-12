using Microsoft.Extensions.Configuration;
using MongoDB.Driver;

namespace OpenSleigh.Persistence.Mongo.Tests.Fixtures;

public class DbFixture : IAsyncLifetime
{
    private readonly string _connectionString;
    private readonly MongoClient _client;
    private readonly List<IMongoDatabase> _dbs = new();

    public DbFixture()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        _connectionString = configuration.GetConnectionString("mongo");
        if (string.IsNullOrWhiteSpace(_connectionString))
            throw new ArgumentException("invalid connection string");

        _client = new MongoClient(_connectionString);
    }

    public IDbContext CreateDbContext(string? dbSuffix = null)
    {
        dbSuffix ??= Guid.NewGuid().ToString();
        var dbName = $"openSleigh_{dbSuffix}";
        var db = _client.GetDatabase(dbName);
        var dbContext = new DbContext(db);
        _dbs.Add(db);
        return dbContext;
    }

    public string ConnectionString => _connectionString;

    /// <summary>
    /// returns the name of a database that does not exist yet and is dropped when the fixture is disposed.
    /// </summary>
    public string CreateDatabaseName()
    {
        var dbName = $"openSleigh_{Guid.NewGuid()}";
        _dbs.Add(_client.GetDatabase(dbName));
        return dbName;
    }

    public Task InitializeAsync()
        => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_client is null)
            return;
        foreach (var db in _dbs)
            await _client.DropDatabaseAsync(db.DatabaseNamespace.DatabaseName)
                        .ConfigureAwait(false);
    }
}