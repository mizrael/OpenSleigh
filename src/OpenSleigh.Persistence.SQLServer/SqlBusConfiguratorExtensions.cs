using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Outbox;
using OpenSleigh.Persistence.SQL;
using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Persistence.SQLServer;

[ExcludeFromCodeCoverage]
public static class SqlBusConfiguratorExtensions
{
    public static IBusConfigurator UseSqlServerPersistence(
        this IBusConfigurator busConfigurator, SqlConfiguration config)
    {
        busConfigurator.Services
            .AddSingleton(config.SagaRepositoryOptions)
            .AddSingleton(config.OutboxRepositoryOptions)
            .AddDbContextPool<SagaDbContext>(builder =>
            {
                builder.UseSqlServer(config.ConnectionString);
            })
            .AddTransient<SagaDbContext>(ctx => ctx.GetRequiredService<SagaDbContext>())                        
            .AddTransient<IOutboxRepository, SqlOutboxRepository>()
            .AddTransient<ISagaStateRepository, SqlSagaStateRepository>();
        
        return busConfigurator;
    }
}