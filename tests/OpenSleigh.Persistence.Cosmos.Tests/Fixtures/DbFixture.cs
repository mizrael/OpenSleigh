﻿using System;
using System.Security.Authentication;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;

namespace OpenSleigh.Persistence.Cosmos.Tests.Fixtures
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
                .AddUserSecrets<DbFixture>()
                .Build();

            this.ConnectionString = configuration.GetConnectionString("cosmos");
            if (string.IsNullOrWhiteSpace(this.ConnectionString))
                throw new ArgumentException("invalid connection string");

            var settings = MongoClientSettings.FromUrl(new MongoUrl(this.ConnectionString));
            settings.SslSettings = new SslSettings() { EnabledSslProtocols = SslProtocols.Tls12 };

            _client = new MongoClient(settings);

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