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
            busConfigurator.Services
                .AddSingleton(config.SagaRepositoryOptions)
                .AddSingleton(config.OutboxRepositoryOptions)
                .AddDbContext<SagaDbContext>(builder =>
                {
                    builder.UseSqlServer(config.ConnectionString);
                }, contextLifetime: ServiceLifetime.Transient)
                .AddTransient<ISagaDbContext>(ctx => ctx.GetRequiredService<SagaDbContext>())                        
                .AddTransient<IOutboxRepository, SqlOutboxRepository>()
                .AddTransient<ISagaStateRepository, SqlSagaStateRepository>();
            
            return busConfigurator;
        }
    }
}