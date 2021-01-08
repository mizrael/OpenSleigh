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
            where TS : Saga<TD> where TD : SagaState
        {
            var sagaType = typeof(TS);
            var sagaStateType = typeof(TD);

            Services.AddScoped<TS>();

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

                //TODO: move this check into SagaTypeResolver
                if (messageType.IsAssignableTo(typeof(ICommand)))
                {
                    var commandHandlerType = typeof(IHandleMessage<>).MakeGenericType(messageType);
                    if (Services.Any(sd => sd.ServiceType == commandHandlerType))
                        throw new TypeLoadException(
                            $"there is already one handler registered for command type '{messageType.FullName}'");
                }

                _typeResolver.Register(messageType, (sagaType, sagaStateType));

                Services.AddTransient(i, sagaType);
            }

            Services.AddSingleton(typeof(ISagaStateService<,>).MakeGenericType(sagaType, sagaStateType),
                                 typeof(SagaStateService<,>).MakeGenericType(sagaType, sagaStateType));

            Services.AddSingleton(typeof(ISagaRunner<,>).MakeGenericType(sagaType, sagaStateType),
                                  typeof(SagaRunner<,>).MakeGenericType(sagaType, sagaStateType));

            Services.AddSingleton(typeof(ISagaFactory<,>).MakeGenericType(sagaType, sagaStateType),
                                typeof(DefaultSagaFactory<,>).MakeGenericType(sagaType, sagaStateType));

            return new SagaConfigurator<TS, TD>(Services);
        }
    }
}