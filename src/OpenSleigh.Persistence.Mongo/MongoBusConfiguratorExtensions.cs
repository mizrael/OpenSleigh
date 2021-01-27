using System.Diagnostics.CodeAnalysis;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Persistence;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace OpenSleigh.Persistence.Mongo
{
    [ExcludeFromCodeCoverage]
    public record MongoConfiguration(string ConnectionString, string DbName)
    {
        public MongoConfiguration(string connectionString, string dbName,
            MongoSagaStateRepositoryOptions sagaRepositoryOptions,
            MongoOutboxRepositoryOptions outboxRepositoryOptions) : this(connectionString, dbName)
        {
            this.SagaRepositoryOptions = sagaRepositoryOptions;
            this.OutboxRepositoryOptions = outboxRepositoryOptions;
        }
            
        public MongoSagaStateRepositoryOptions SagaRepositoryOptions { get; init; } =
            MongoSagaStateRepositoryOptions.Default;

        public MongoOutboxRepositoryOptions OutboxRepositoryOptions { get; init; } =
            MongoOutboxRepositoryOptions.Default;
    }

    [ExcludeFromCodeCoverage]
    public static class MongoBusConfiguratorExtensions
    {
        public static IBusConfigurator UseMongoPersistence(
            this IBusConfigurator busConfigurator, MongoConfiguration config)
        {
            busConfigurator.Services
                .AddSingleton<IMongoClient>(ctx => new MongoClient(connectionString: config.ConnectionString))
                .AddSingleton(ctx =>
                {
                    var client = ctx.GetRequiredService<IMongoClient>();
                    var database = client.GetDatabase(config.DbName);
                    return database;
                })
                .AddSingleton(config.SagaRepositoryOptions)
                .AddSingleton(config.OutboxRepositoryOptions)
                
                .AddScoped<IDbContext, DbContext>()
                .AddScoped<ITransactionManager, MongoTransactionManager>()
                .AddScoped<ISagaStateRepository, MongoSagaStateRepository>()
                .AddScoped<IOutboxRepository, MongoOutboxRepository>();
            return busConfigurator;
        }
    }
}