using OpenSleigh.Core;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Persistence;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Channels;

namespace OpenSleigh.Persistence.InMemory
{
    public static class InMemorySagaConfiguratorExtensions
    {
        public static ISagaConfigurator<TS, TD> UseInMemoryPersistence<TS, TD>(this ISagaConfigurator<TS, TD> sagaConfigurator)
            where TS : Saga<TD>
            where TD : SagaState
        {
            sagaConfigurator.Services.AddSingleton(typeof(ISagaStateRepository), typeof(InMemorySagaStateRepository));
            sagaConfigurator.Services.AddSingleton<IUnitOfWork, InMemoryUnitOfWork>();

            return sagaConfigurator;
        }

        public static ISagaConfigurator<TS, TD> UseInMemoryTransport<TS, TD>(this ISagaConfigurator<TS, TD> sagaConfigurator)
            where TS : Saga<TD>
            where TD : SagaState
        {
            var sagaType = typeof(TS);
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

                var rawMethod = typeof(Channel).GetMethod(nameof(Channel.CreateUnbounded), new Type[] { });
                var method = rawMethod.MakeGenericMethod(messageType);
                dynamic channel = method.Invoke(null, null);

                sagaConfigurator.Services.AddSingleton(typeof(Channel<>).MakeGenericType(messageType), (object)channel);

                sagaConfigurator.Services.AddSingleton(typeof(ChannelWriter<>).MakeGenericType(messageType), (object)channel.Writer);

                sagaConfigurator.Services.AddSingleton(typeof(ChannelReader<>).MakeGenericType(messageType), (object)channel.Reader);

                sagaConfigurator.Services.AddSingleton(typeof(IPublisher<>).MakeGenericType(messageType),
                                                    typeof(InMemoryPublisher<>).MakeGenericType(messageType));

                sagaConfigurator.Services.AddSingleton(typeof(ISubscriber<>).MakeGenericType(messageType),
                                                    typeof(InMemorySubscriber<>).MakeGenericType(messageType));
            }

            return sagaConfigurator;
        }
    }
}