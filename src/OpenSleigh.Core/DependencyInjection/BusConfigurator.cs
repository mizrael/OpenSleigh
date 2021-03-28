using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenSleigh.Core.ExceptionPolicies;
using OpenSleigh.Core.Messaging;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using OpenSleigh.Core.Utils;

[assembly: InternalsVisibleTo("UnitTests")]
namespace OpenSleigh.Core.DependencyInjection
{
    [ExcludeFromCodeCoverage]
    internal class BusConfigurator : IBusConfigurator
    {
        private readonly ISagaTypeResolver _typeResolver;
        private readonly SystemInfo _systemInfo;
        
        public IServiceCollection Services { get; }

        public BusConfigurator(IServiceCollection services, ISagaTypeResolver typeResolver, SystemInfo systemInfo)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
            _typeResolver = typeResolver ?? throw new ArgumentNullException(nameof(typeResolver));
            _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));
        }

        public IBusConfigurator SetPublishOnly(bool value = true)
        {
            _systemInfo.PublishOnly = value;
            return this;
        }

        public IBusConfigurator WithOutboxProcessorOptions(OutboxProcessorOptions options)
        {
            if (options == null) 
                throw new ArgumentNullException(nameof(options));

            this.Services.Replace(ServiceDescriptor.Singleton(options));
            
            return this;
        }

        public IBusConfigurator WithOutboxCleanerOptions(OutboxCleanerOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            this.Services.Replace(ServiceDescriptor.Singleton(options));

            return this;
        }

        public IBusConfigurator AddMessageHandlers(IEnumerable<Assembly> sourceAssemblies)                        
        {
            if (sourceAssemblies is null)           
                throw new ArgumentNullException(nameof(sourceAssemblies));       
            
            foreach(var assembly in sourceAssemblies)
            {
                var types = assembly.GetTypes();
                foreach(var type in types)
                {
                    if (type.IsSaga())
                        continue;

                    var messageTypes = type.GetHandledMessageTypes();
                    foreach (var messageType in messageTypes)
                        Services.AddTransient(
                            typeof(IHandleMessage<>).MakeGenericType(messageType), type);
                }
            }

            return this;
        }

        public ISagaConfigurator<TS, TD> AddSaga<TS, TD>()
           where TD : SagaState
           where TS : Saga<TD>
        {
            var hasMessages = _typeResolver.Register<TS, TD>();

            if (hasMessages)
            {
                Services.AddTransient<TS>()
                        .AddTransient<ISagaPolicyFactory<TS>, DefaultSagaPolicyFactory<TS>>()
                        .AddTransient<ISagaFactory<TS, TD>, DefaultSagaFactory<TS, TD>>()
                        .AddTransient<ISagaStateService<TS, TD>, SagaStateService<TS, TD>>()
                        .AddTransient<ISagaRunner<TS, TD>, SagaRunner<TS, TD>>()
                        .AddTransient<ISagaStateFactory<TD>, DefaultSagaStateFactory<TD>>();
            }

            return new SagaConfigurator<TS, TD>(Services);
        }
    }
}