using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.InMemory.Messaging;
using OpenSleigh.InMemory.Outbox;
using OpenSleigh.Transport;
using OpenSleigh.Outbox;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;

namespace OpenSleigh.InMemory;

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
                                .AddSingleton<Channel<MessageEnvelope>>(ctx => Channel.CreateBounded<MessageEnvelope>(options.SubscriberMaxMessagesBatchSize))
                                .AddSingleton<ChannelReader<MessageEnvelope>>(ctx =>
                                {
                                    var channel = ctx.GetRequiredService<Channel<MessageEnvelope>>();
                                    return channel.Reader;
                                }).AddSingleton<ChannelWriter<MessageEnvelope>>(ctx =>
                                {
                                    var channel = ctx.GetRequiredService<Channel<MessageEnvelope>>();
                                    return channel.Writer;
                                })
                                .AddSingleton(typeof(IMessageSubscriber<>), typeof(InMemorySubscriber<>));

        return busConfigurator;
    }
}
