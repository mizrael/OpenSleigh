using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Outbox;
using OpenSleigh.Queries;
using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Persistence.Mongo;

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
            .AddTransient<ISagaStateRepository, MongoSagaStateRepository>()
            .AddTransient<IOutboxRepository, MongoOutboxRepository>()
            .AddTransient<ISagaStateQuery, MongoSagaStateQuery>();
        return busConfigurator;
    }
}