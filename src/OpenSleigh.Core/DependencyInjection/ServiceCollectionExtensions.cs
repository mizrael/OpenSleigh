using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using OpenSleigh.Core.BackgroundServices;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Utils;

namespace OpenSleigh.Core.DependencyInjection
{
    [ExcludeFromCodeCoverage]
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddOpenSleigh(this IServiceCollection services, Action<IBusConfigurator> configure = null)
        {
            var systemInfo = SystemInfo.New();

            var typeResolver = new TypeResolver();
            var sagaTypeResolver = new SagaTypeResolver(typeResolver);

            services.AddTransient<IMessageBus, DefaultMessageBus>()
                .AddSingleton(systemInfo)
                .AddSingleton<ISagaTypeResolver>(sagaTypeResolver)
                .AddSingleton<ISagasRunner, SagasRunner>()
                .AddSingleton<ITypesCache, TypesCache>()
                .AddSingleton<ITypeResolver>(typeResolver)
                .AddSingleton<ISerializer, JsonSerializer>()
                .AddSingleton<IMessageContextFactory, DefaultMessageContextFactory>()
                .AddSingleton<IMessageProcessor, MessageProcessor>()
                .AddHostedService<SubscribersBackgroundService>()

                .AddTransient<IOutboxProcessor, OutboxProcessor>()
                .AddSingleton(OutboxProcessorOptions.Default) //TODO: this should be configurable
                .AddHostedService<OutboxBackgroundService>()
                
                .AddTransient<IOutboxCleaner, OutboxCleaner>()
                .AddSingleton(OutboxCleanerOptions.Default) //TODO: this should be configurable
                .AddHostedService<OutboxCleanerBackgroundService>();

            var builder = new BusConfigurator(services, sagaTypeResolver, systemInfo);
            configure?.Invoke(builder);
            
            return services;
        }
        
        public static IServiceCollection AddBusSubscriber(this IServiceCollection services, Type subscriberType)
        {
            if (!services.Any(s => s.ImplementationType == subscriberType))
                services.AddSingleton(typeof(ISubscriber), subscriberType);
            return services;
        }
    }

}