using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Persistence;
using OpenSleigh.Persistence.SQL;

namespace OpenSleigh.Persistence.PostgreSQL
{
    [ExcludeFromCodeCoverage]
    public static class SqlBusConfiguratorExtensions
    {
        public static IBusConfigurator UsePostgreSqlPersistence(
            this IBusConfigurator busConfigurator, SqlConfiguration config)
        {
            busConfigurator.Services.AddDbContextPool<SagaDbContext>(builder =>
            {
                builder.UseNpgsql(config.ConnectionString);
            }).AddScoped<ISagaDbContext>(ctx => ctx.GetRequiredService<SagaDbContext>())
            .AddScoped<ITransactionManager, SqlTransactionManager>()
            .AddSingleton(config.SagaRepositoryOptions)
            .AddSingleton(config.OutboxRepositoryOptions)
            .AddScoped<IOutboxRepository, SqlOutboxRepository>()
            .AddScoped<ISagaStateRepository, SqlSagaStateRepository>();
            
            return busConfigurator;
        }
    }
}