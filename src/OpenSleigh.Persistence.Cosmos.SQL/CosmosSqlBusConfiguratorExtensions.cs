using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Persistence;

namespace OpenSleigh.Persistence.Cosmos.SQL
{
    [ExcludeFromCodeCoverage]
    public record CosmosSqlConfiguration(string ConnectionString,
        string DbName,
        CosmosSqlSagaStateRepositoryOptions SagaRepositoryOptions,
        CosmosSqlOutboxRepositoryOptions OutboxRepositoryOptions)
    {
        public CosmosSqlConfiguration(string connectionString, string dbName) : this(connectionString, dbName, CosmosSqlSagaStateRepositoryOptions.Default, CosmosSqlOutboxRepositoryOptions.Default) { }
    }
    
    [ExcludeFromCodeCoverage]
    public static class CosmosSqlBusConfiguratorExtensions
    {
        public static IBusConfigurator UseCosmosSqlPersistence(
            this IBusConfigurator busConfigurator, CosmosSqlConfiguration config)
        {
            busConfigurator.Services.AddDbContextPool<SagaDbContext>(builder =>
            {
                builder.UseCosmos(config.ConnectionString, config.DbName);
            }).AddScoped<ISagaDbContext>(ctx => ctx.GetRequiredService<SagaDbContext>())
            .AddScoped<ITransactionManager, CosmosSqlTransactionManager>()
            .AddSingleton(config.SagaRepositoryOptions)
            .AddSingleton(config.OutboxRepositoryOptions)
            .AddScoped<IOutboxRepository, CosmosSqlOutboxRepository>()
            .AddScoped<ISagaStateRepository, CosmosSqlSagaStateRepository>();
            
            return busConfigurator;
        }
    }
}