using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Outbox;
using OpenSleigh.Persistence.SQL;
using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Persistence.SQLServer
{
    [ExcludeFromCodeCoverage]
    public static class SqlBusConfiguratorExtensions
    {
        public static IBusConfigurator UseSqlServerPersistence(
            this IBusConfigurator busConfigurator, SqlConfiguration config)
        {
            busConfigurator.Services.AddDbContextPool<SagaDbContext>(builder =>
            {
                builder.UseSqlServer(config.ConnectionString);
            }).AddScoped<ISagaDbContext>(ctx => ctx.GetRequiredService<SagaDbContext>())            
            .AddSingleton(config.SagaRepositoryOptions)
            .AddSingleton(config.OutboxRepositoryOptions)
            .AddScoped<IOutboxRepository, SqlOutboxRepository>()
            .AddScoped<ISagaStateRepository, SqlSagaStateRepository>();
            
            return busConfigurator;
        }
    }
}