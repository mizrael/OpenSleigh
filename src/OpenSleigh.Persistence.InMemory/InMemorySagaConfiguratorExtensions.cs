using OpenSleigh.Core;
using OpenSleigh.Core.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading.Channels;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Utils;
using OpenSleigh.Persistence.InMemory.Messaging;

namespace OpenSleigh.Persistence.InMemory
{
    [ExcludeFromCodeCoverage]
    public static class InMemorySagaConfiguratorExtensions
    {
        private static readonly MethodInfo RawRegisterMessageMethod = typeof(InMemorySagaConfiguratorExtensions)
            .GetMethod(nameof(RegisterMessage), BindingFlags.Static | BindingFlags.NonPublic);
        
        public static ISagaConfigurator<TS, TD> UseInMemoryTransport<TS, TD>(this ISagaConfigurator<TS, TD> sagaConfigurator)
            where TS : Saga<TD>
            where TD : SagaState
        {
            var messageTypes = SagaUtils<TS, TD>.GetHandledMessageTypes();
            foreach (var messageType in messageTypes)
            {
                var registerMessageMethod = RawRegisterMessageMethod.MakeGenericMethod(messageType);
                registerMessageMethod.Invoke(null, new[] { sagaConfigurator.Services });
            }

            return sagaConfigurator;
        }

        private static void RegisterMessage<TM>(IServiceCollection services) where TM : IMessage
        {
            if (services.Any(sd => sd.ServiceType == typeof(Channel<TM>)))
                return;

            services.AddSingleton<Channel<TM>>(ctx => Channel.CreateUnbounded<TM>())
                .AddSingleton<ChannelReader<TM>>(ctx =>
                {
                    var channel = ctx.GetService<Channel<TM>>();
                    return channel?.Reader;
                }).AddSingleton<ChannelWriter<TM>>(ctx =>
                {
                    var channel = ctx.GetService<Channel<TM>>();
                    return channel?.Writer;
                }).AddBusSubscriber(typeof(InMemorySubscriber<TM>));
        }
    }
}