using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.InMemory.Messaging;
using OpenSleigh.InMemory.Outbox;
using OpenSleigh.Messaging;
using OpenSleigh.Outbox;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;

namespace OpenSleigh.InMemory
{
    [ExcludeFromCodeCoverage]
    public static class InMemoryBusConfiguratorExtensions
    {
        public static IBusConfigurator UseInMemoryPersistence(
            this IBusConfigurator busConfigurator)
        {
            busConfigurator.Services.AddSingleton<ISagaStateRepository, InMemorySagaStateRepository>()
                                    .AddSingleton<IOutboxRepository, InMemoryOutboxRepository>();

            return busConfigurator;
        }

        public static IBusConfigurator UseInMemoryTransport(
            this IBusConfigurator busConfigurator,
            InMemorySagaOptions? options = null)
        {
            options ??= InMemorySagaOptions.Defaults;

            busConfigurator.Services.AddSingleton<IPublisher, InMemoryPublisher>()
                                    .AddSingleton(options)
                                    .AddSingleton<Channel<OutboxMessage>>(ctx => Channel.CreateBounded<OutboxMessage>(options.SubscriberMaxMessagesBatchSize))
                                    .AddSingleton<ChannelReader<OutboxMessage>>(ctx =>
                                    {
                                        var channel = ctx.GetRequiredService<Channel<OutboxMessage>>();
                                        return channel.Reader;
                                    }).AddSingleton<ChannelWriter<OutboxMessage>>(ctx =>
                                    {
                                        var channel = ctx.GetRequiredService<Channel<OutboxMessage>>();
                                        return channel.Writer;
                                    }).AddSingleton<ISubscriber, InMemorySubscriber>();

            return busConfigurator;
        }
    }
}
