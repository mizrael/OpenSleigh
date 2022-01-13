using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Utils;
using OpenSleigh.Persistence.InMemory.Messaging;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading.Channels;

namespace OpenSleigh.Persistence.InMemory
{
    public record InMemorySagaOptions
    {
        /// <summary>
        /// max size of the message batches processed concurrently by each subscriber.
        /// </summary>
        public int SubscriberMaxMessagesBatchSize { get; }

        public InMemorySagaOptions(int messagesBatchSize)
        {
            SubscriberMaxMessagesBatchSize = messagesBatchSize;
        }

        public static readonly InMemorySagaOptions Defaults = new InMemorySagaOptions(5);
    }

    [ExcludeFromCodeCoverage]
    public static class InMemorySagaConfiguratorExtensions
    {
        private static readonly MethodInfo RawRegisterMessageMethod = typeof(InMemorySagaConfiguratorExtensions)
            .GetMethod(nameof(RegisterMessage), BindingFlags.Static | BindingFlags.NonPublic);
        
        public static ISagaConfigurator<TS, TD> UseInMemoryTransport<TS, TD>(this ISagaConfigurator<TS, TD> sagaConfigurator,
            InMemorySagaOptions options = null)
            where TS : Saga<TD>
            where TD : SagaState
        {
            options ??= InMemorySagaOptions.Defaults;

            sagaConfigurator.Services.AddSingleton(options);

            var messageTypes = typeof(TS).GetHandledMessageTypes();
            foreach (var messageType in messageTypes)
            {
                var registerMessageMethod = RawRegisterMessageMethod.MakeGenericMethod(messageType);
                registerMessageMethod.Invoke(null, new object[] { sagaConfigurator.Services, options });
            }

            return sagaConfigurator;
        }

        private static void RegisterMessage<TM>(IServiceCollection services, InMemorySagaOptions options) where TM : IMessage
        {
            if (services.Any(sd => sd.ServiceType == typeof(Channel<TM>)))
                return;

            services.AddSingleton<Channel<TM>>(ctx => Channel.CreateBounded<TM>(options.SubscriberMaxMessagesBatchSize))
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