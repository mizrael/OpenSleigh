using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenSleigh.Core.ExceptionPolicies;
using OpenSleigh.Core.Messaging;
using System.Collections.Generic;
using System.Reflection;
using OpenSleigh.Core.Utils;

namespace OpenSleigh.Core.DependencyInjection
{
    [ExcludeFromCodeCoverage]
    internal class BusConfigurator : IBusConfigurator
    {
        private readonly ISagaTypeResolver _sagaTypeResolver;
        private readonly ITypeResolver _typeResolver;
        private readonly SystemInfo _systemInfo;
        
        public IServiceCollection Services { get; }

        public BusConfigurator(IServiceCollection services, 
            ISagaTypeResolver sagaTypeResolver, 
            ITypeResolver typeResolver,
            SystemInfo systemInfo)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
            _sagaTypeResolver = sagaTypeResolver ?? throw new ArgumentNullException(nameof(sagaTypeResolver));
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

        public IBusConfigurator WithTransportSerializer<T>(T serializer = null) where T : class, ITransportSerializer
        {
            if(serializer is null)
                this.Services.Replace(ServiceDescriptor.Singleton<ITransportSerializer, T>());
            else
                this.Services.Replace(ServiceDescriptor.Singleton<ITransportSerializer, T>(_ => serializer));

            return this;
        }

        public IBusConfigurator WithPersistenceSerializer<T>(T serializer = null) where T : class, IPersistenceSerializer
        {
            if (serializer is null)
                this.Services.Replace(ServiceDescriptor.Singleton<IPersistenceSerializer, T>());
            else
                this.Services.Replace(ServiceDescriptor.Singleton<IPersistenceSerializer, T>(_ => serializer));

            return this;
        }

        public IMessageHandlerConfigurator<TM> AddMessageHandlers<TM>(IEnumerable<Assembly> sourceAssemblies)       
            where TM : IMessage                  
        {
            if (sourceAssemblies is null)           
                throw new ArgumentNullException(nameof(sourceAssemblies));

            var hasHandlers = false;
            
            foreach(var assembly in sourceAssemblies)
            {
                var types = assembly.GetTypes();
                foreach(var type in types)
                {
                    if (type.IsSaga() || !type.CanHandleMessage<TM>())
                        continue;

                    Services.AddTransient(typeof(IHandleMessage<TM>), type);
                    
                    hasHandlers = true;
                }
            }

            if (hasHandlers)
                _typeResolver.Register(typeof(TM));

            return new MessageHandlerConfigurator<TM>(this.Services);
        }

        public ISagaConfigurator<TS, TD> AddSaga<TS, TD>()
           where TD : SagaState
           where TS : Saga<TD>
        {
            var hasMessages = _sagaTypeResolver.Register<TS, TD>();

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