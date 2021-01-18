using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core.BackgroundServices;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Persistence;

namespace OpenSleigh.Core.DependencyInjection
{
    [ExcludeFromCodeCoverage]
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddOpenSleigh(this IServiceCollection services, Action<IBusConfigurator> configure = null)
        {
            var typeResolver = new TypeResolver();
            var sagaTypeResolver = new SagaTypeResolver(typeResolver);

            services.AddSingleton<ISagaTypeResolver>(sagaTypeResolver)
                .AddScoped<ISagasRunner, SagasRunner>()
                .AddSingleton<ITypesCache, TypesCache>()
                .AddSingleton<ITypeResolver>(typeResolver)
                .AddSingleton<IMessageContextFactory, DefaultMessageContextFactory>()
                .AddScoped<IMessageBus, DefaultMessageBus>()
                .AddScoped<IMessageProcessor, MessageProcessor>()
                .AddSingleton<IOutboxProcessor>(ctx =>
                {
                    var repo = ctx.GetRequiredService<IOutboxRepository>();
                    var publisher = ctx.GetRequiredService<IPublisher>();
                    var logger = ctx.GetRequiredService<ILogger<OutboxProcessor>>();
                    return new OutboxProcessor(repo, publisher, OutboxProcessorOptions.Default, logger);
                })
                .AddSingleton<IOutboxCleaner>(ctx =>
                {
                    var repo = ctx.GetRequiredService<IOutboxRepository>();
                    return new OutboxCleaner(repo, OutboxCleanerOptions.Default);
                }).AddHostedService<SubscribersBackgroundService>()
                .AddHostedService<OutboxBackgroundService>()
                .AddHostedService<OutboxCleanerBackgroundService>();

            var builder = new BusConfigurator(services, sagaTypeResolver);
            configure?.Invoke(builder);
            
            return services;
        }
    }

}