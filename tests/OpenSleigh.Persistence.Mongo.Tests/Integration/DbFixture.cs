using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using System;

namespace OpenSleigh.Persistence.Mongo.Tests.Integration
{
    public class DbFixture : IDisposable
    {
        private MongoClient _client;
        private IMongoDatabase _db;

        public IDbContext DbContext { get; }

        public DbFixture()
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddEnvironmentVariables()
                .Build();

            var connStr = configuration.GetConnectionString("mongo");
            if (string.IsNullOrWhiteSpace(connStr))
                throw new ArgumentException("invalid cosmos connection string");

            _client = new MongoClient(connStr);

            var dbName = $"mongoLocks_{Guid.NewGuid()}";
            _db = _client.GetDatabase(dbName);

            DbContext = new DbContext(_db);
        }

        public void Dispose()
        {
            _client?.DropDatabase(_db.DatabaseNamespace.DatabaseName);
        }
    }
}
