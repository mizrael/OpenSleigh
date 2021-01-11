using System;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;

namespace OpenSleigh.Persistence.Mongo.Tests.Fixtures
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

           this.ConnectionString = configuration.GetConnectionString("mongo");
            if (string.IsNullOrWhiteSpace(this.ConnectionString))
                throw new ArgumentException("invalid connection string");

            _client = new MongoClient(this.ConnectionString);

            this.DbName = $"openSleigh_{Guid.NewGuid()}";
            _db = _client.GetDatabase(this.DbName);

            DbContext = new DbContext(_db);
        }
        
        public string ConnectionString { get; init; }
        public string DbName { get; init; }

        public void Dispose()
        {
            _client?.DropDatabase(_db.DatabaseNamespace.DatabaseName);
        }
    }
}
