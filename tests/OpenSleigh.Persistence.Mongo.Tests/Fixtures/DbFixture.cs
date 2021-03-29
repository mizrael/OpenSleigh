using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;

namespace OpenSleigh.Persistence.Mongo.Tests.Fixtures
{
    public class DbFixture : IDisposable
    {
        private readonly MongoClient _client;
        private readonly List<IMongoDatabase> _dbs = new();

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
        }

        public (IDbContext db, string name) CreateDbContext()
        {
            var dbName = $"openSleigh_{Guid.NewGuid()}";
            var db = _client.GetDatabase(dbName);
            var dbContext = new DbContext(db);
            _dbs.Add(db);
            return (dbContext, dbName);
        }

        public string ConnectionString { get; }
        
        public void Dispose()
        {
            if (_client is null)
                return;
            foreach(var db in _dbs)
                _client.DropDatabase(db.DatabaseNamespace.DatabaseName);
        }
    }
}
