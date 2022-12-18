using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Messaging;
using System.Diagnostics.CodeAnalysis;
using OpenSleigh.Utils;
using OpenSleigh.Outbox;
using Microsoft.Extensions.Configuration;

namespace OpenSleigh.DependencyInjection
{
    [ExcludeFromCodeCoverage]
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddOpenSleigh(
            this IServiceCollection services, 
            Action<IBusConfigurator>? busConfigurator = null,
            IConfigurationRoot? configuration = null)
        {
            SystemInfo systemInfo = SystemInfo.Create(configuration);

            var typeResolver = new TypeResolver();
            var sagaDescriptorResolver = new SagaDescriptorsResolver(typeResolver);

            services
                .AddSingleton(systemInfo)
                .AddSingleton<ISystemInfo>(systemInfo)
                .AddSingleton<ISerializer, JsonSerializer>()
                .AddSingleton<ITypeResolver>(typeResolver)
                .AddSingleton<ISagaDescriptorsResolver>(sagaDescriptorResolver)

                .AddTransient<IMessageBus, DefaultMessageBus>()
                .AddTransient<ISagaRunner, SagaRunner>()
                .AddTransient<ISagaExecutionContextFactory, SagaExecutionContextFactory>()                
                .AddTransient<IMessageHandlerFactory, DefaultMessageHandlerFactory>()
                .AddTransient<IMessageProcessor, MessageProcessor>()
                .AddHostedService<SubscribersBackgroundService>()

                .AddTransient<IOutboxProcessor, OutboxProcessor>()
                .AddSingleton(OutboxProcessorOptions.Default)
                .AddHostedService<OutboxBackgroundService>()
                ;

            var builder = new BusConfigurator(services, systemInfo, sagaDescriptorResolver);
            busConfigurator?.Invoke(builder);

            return services;
        }      
    }
}