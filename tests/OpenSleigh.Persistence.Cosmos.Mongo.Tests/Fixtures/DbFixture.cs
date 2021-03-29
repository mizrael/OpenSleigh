using System;
using System.Collections.Generic;
using System.Security.Authentication;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;

namespace OpenSleigh.Persistence.Cosmos.Mongo.Tests.Fixtures
{
    public class DbFixture : IDisposable
    {
        private readonly MongoClient _client;
        private readonly List<string> _dbNames = new();
        public DbFixture()
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddEnvironmentVariables()
                .AddUserSecrets<DbFixture>()
                .Build();

            this.ConnectionString = configuration.GetConnectionString("cosmos");
            if (string.IsNullOrWhiteSpace(this.ConnectionString))
                throw new ArgumentException("invalid connection string");

            var settings = MongoClientSettings.FromUrl(new MongoUrl(this.ConnectionString));
            settings.SslSettings = new SslSettings() { EnabledSslProtocols = SslProtocols.Tls12 };

            _client = new MongoClient(settings);
        }

        public (IDbContext dbContext, string dbName) CreateDbContext()
        {
            var dbName = $"openSleigh_{Guid.NewGuid()}";
            var db = _client.GetDatabase(dbName);

            var dbContext = new DbContext(db);
            
            _dbNames.Add(dbName);
            
            return (dbContext, dbName);
        }
        
        public string ConnectionString { get; init; }

        public void Dispose()
        {
            if (_client is null)
                return;
            foreach(var dbName in _dbNames)
                _client.DropDatabase(dbName);
        }
    }
}
