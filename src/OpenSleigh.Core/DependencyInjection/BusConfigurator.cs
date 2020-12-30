using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("UnitTests")]
namespace OpenSleigh.Core.DependencyInjection
{
    internal class BusConfigurator : IBusConfigurator
    {
        private readonly ISagaTypeResolver _typeResolver;
        private readonly IServiceCollection _services;

        public BusConfigurator(IServiceCollection services, ISagaTypeResolver typeResolver)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _typeResolver = typeResolver ?? throw new ArgumentNullException(nameof(typeResolver));
        }

        public ISagaConfigurator<TS, TD> AddSaga<TS, TD>() 
            where TS : Saga<TD> where TD : SagaState
        {
            var sagaType = typeof(TS);
            var sagaStateType = typeof(TD);

            _services.AddScoped<TS>();

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

                if (messageType.IsAssignableTo(typeof(ICommand)))
                {
                    var commandHandlerType = typeof(IHandleMessage<>).MakeGenericType(messageType);
                    if (_services.Any(sd => sd.ServiceType == commandHandlerType))
                        throw new TypeLoadException(
                            $"there is already one handler registered for command type '{messageType.FullName}'");
                }

                _typeResolver.Register(messageType, (sagaType, sagaStateType));

                _services.AddTransient(i, sagaType);
            }

            _services.AddSingleton(typeof(ISagaStateService<,>).MakeGenericType(sagaType, sagaStateType),
                                 typeof(SagaStateService<,>).MakeGenericType(sagaType, sagaStateType));

            _services.AddSingleton(typeof(ISagaRunner<,>).MakeGenericType(sagaType, sagaStateType),
                                  typeof(SagaRunner<,>).MakeGenericType(sagaType, sagaStateType));

            _services.AddSingleton(typeof(ISagaFactory<,>).MakeGenericType(sagaType, sagaStateType),
                                typeof(DefaultSagaFactory<,>).MakeGenericType(sagaType, sagaStateType));

            return new SagaConfigurator<TS, TD>(_services);
        }
    }
}