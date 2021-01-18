using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using OpenSleigh.Core.Messaging;

[assembly: InternalsVisibleTo("UnitTests")]
namespace OpenSleigh.Core.DependencyInjection
{
    [ExcludeFromCodeCoverage]
    internal class BusConfigurator : IBusConfigurator
    {
        private readonly ISagaTypeResolver _typeResolver;
        public IServiceCollection Services { get; }

        public BusConfigurator(IServiceCollection services, ISagaTypeResolver typeResolver)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
            _typeResolver = typeResolver ?? throw new ArgumentNullException(nameof(typeResolver));
        }

        public ISagaConfigurator<TS, TD> AddSaga<TS, TD>()
            where TD : SagaState
            where TS : Saga<TD>
        {
            bool hasMessages = false;
            
            var sagaType = typeof(TS);
            var sagaStateType = typeof(TD);
            
            var messageHandlerType = typeof(IHandleMessage<>).GetGenericTypeDefinition();

            var interfaces = sagaType.GetInterfaces();
            foreach (var i in interfaces)
            {
                if (!i.IsGenericType)
                    continue;

                var openGeneric = i.GetGenericTypeDefinition();
                if (!openGeneric.IsAssignableFrom(messageHandlerType))
                    continue;

                var messageType = i.GetGenericArguments().First();
                
                _typeResolver.Register(messageType, (sagaType, sagaStateType));

                Services.AddTransient(i, sagaType);

                hasMessages = true;
            }

            if (hasMessages)
            {
                Services.AddScoped<TS>();
                Services.AddSingleton<ISagaStateService<TS, TD>, SagaStateService<TS, TD>>();
                Services.AddSingleton<ISagaRunner<TS, TD>, SagaRunner<TS, TD>>();
                Services.AddSingleton<ISagaFactory<TS, TD>, DefaultSagaFactory<TS, TD>>();
                Services.AddSingleton<ISagaStateFactory<TD>, DefaultSagaStateFactory<TD>>();
            }

            return new SagaConfigurator<TS, TD>(Services);
        }
    }
}