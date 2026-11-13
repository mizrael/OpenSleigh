using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenSleigh.DependencyInjection;
using OpenSleigh.InMemory.Messaging;
using OpenSleigh.InMemory.Outbox;
using OpenSleigh.Transport;
using OpenSleigh.Outbox;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using OpenSleigh.Persistence;
using OpenSleigh.Queries;

namespace OpenSleigh.InMemory;

[ExcludeFromCodeCoverage]
public static class InMemoryBusConfiguratorExtensions
{
    public static IBusConfigurator UseInMemoryPersistence(
        this IBusConfigurator busConfigurator)
    {
        busConfigurator.Services
            .AddSingleton<InMemorySagaStateRepository>()
            .AddSingleton<ISagaStateRepository>(sp => sp.GetRequiredService<InMemorySagaStateRepository>())
            .AddSingleton<ISagaStateQuery>(sp => sp.GetRequiredService<InMemorySagaStateRepository>())
            .AddSingleton<IOutboxRepository, InMemoryOutboxRepository>();

        // the outbox relay needs one, whatever the transport is
        busConfigurator.Services.TryAddSingleton<ITransactionManager, NoOpTransactionManager>();

        return busConfigurator;
    }

    public static IBusConfigurator UseInMemoryTransport(
        this IBusConfigurator busConfigurator,
        InMemorySagaOptions? options = null)
    {
        options ??= InMemorySagaOptions.Defaults;

        // a persistence configured before the transport keeps its own transaction manager
        busConfigurator.Services.TryAddSingleton<ITransactionManager, NoOpTransactionManager>();

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
                                .AddSingleton<IMessageSubscriber, InMemorySubscriber>();

        return busConfigurator;
    }
}
