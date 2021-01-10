using OpenSleigh.Core;
using OpenSleigh.Core.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading.Channels;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Persistence.InMemory.Messaging;

namespace OpenSleigh.Persistence.InMemory
{
    [ExcludeFromCodeCoverage]
    public static class InMemorySagaConfiguratorExtensions
    {
        private static readonly MethodInfo RawRegisterMessageMethod = typeof(InMemorySagaConfiguratorExtensions)
            .GetMethod("RegisterMessage", BindingFlags.Static | BindingFlags.NonPublic);
        
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

                //TODO: ensure the message was not already registered
                var registerMessageMethod = RawRegisterMessageMethod.MakeGenericMethod(messageType);
                registerMessageMethod.Invoke(null, new[] {sagaConfigurator.Services});
            }

            return sagaConfigurator;
        }

        private static void RegisterMessage<TM>(IServiceCollection services) where TM : IMessage
        {
            var channel = Channel.CreateUnbounded<TM>();
            services.AddSingleton<ChannelReader<TM>>(channel.Reader)
                .AddSingleton<ChannelWriter<TM>>(channel.Writer)
                .AddSingleton<ISubscriber, InMemorySubscriber<TM>>();
        }
    }
}