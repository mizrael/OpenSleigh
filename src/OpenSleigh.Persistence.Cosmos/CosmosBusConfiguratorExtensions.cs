using System.Diagnostics.CodeAnalysis;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Persistence;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using System.Security.Authentication;

namespace OpenSleigh.Persistence.Cosmos
{
    [ExcludeFromCodeCoverage]
    public record CosmosConfiguration(string ConnectionString, string DbName)
    {
        public CosmosConfiguration(string connectionString, string dbName,
            CosmosSagaStateRepositoryOptions sagaRepositoryOptions,
            CosmosOutboxRepositoryOptions outboxRepositoryOptions) : this(connectionString, dbName)
        {
            this.SagaRepositoryOptions = sagaRepositoryOptions;
            this.OutboxRepositoryOptions = outboxRepositoryOptions;
        }
            
        public CosmosSagaStateRepositoryOptions SagaRepositoryOptions { get; init; } =
            CosmosSagaStateRepositoryOptions.Default;

        public CosmosOutboxRepositoryOptions OutboxRepositoryOptions { get; init; } =
            CosmosOutboxRepositoryOptions.Default;
    }

    [ExcludeFromCodeCoverage]
    public static class CosmosBusConfiguratorExtensions
    {
        public static IBusConfigurator UseCosmosPersistence(
            this IBusConfigurator busConfigurator, CosmosConfiguration config)
        {
            busConfigurator.Services
                .AddSingleton<IMongoClient>(ctx =>
                {
                    var settings = MongoClientSettings.FromUrl(new MongoUrl(config.ConnectionString));
                    settings.SslSettings = new SslSettings() { EnabledSslProtocols = SslProtocols.Tls12 };
                    return new MongoClient(settings);
                })
                .AddSingleton(ctx =>
                {
                    var client = ctx.GetRequiredService<IMongoClient>();
                    var database = client.GetDatabase(config.DbName);
                    return database;
                })
                .AddSingleton(config.SagaRepositoryOptions)
                .AddSingleton(config.OutboxRepositoryOptions)
                
                .AddScoped<IDbContext, DbContext>()
                .AddScoped<ITransactionManager, CosmosTransactionManager>()
                .AddScoped<ISagaStateRepository, CosmosSagaStateRepository>()
                .AddScoped<IOutboxRepository, CosmosOutboxRepository>();
            return busConfigurator;
        }
    }
}