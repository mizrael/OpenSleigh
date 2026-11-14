using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenSleigh.Outbox;
using OpenSleigh.Utils;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.RabbitMQ.Tests.Unit;

/// <summary>
/// what a publisher declares on the broker before it publishes, on a substitute of the channel.
/// </summary>
public class RabbitPublisherTests
{
    private static readonly RabbitConfiguration Configuration = new("localhost", "ipsum", "dolor");

    [Fact]
    public async Task PublishAsync_should_declare_the_exchange_only_for_a_message_the_host_does_not_handle()
    {
        var channel = Substitute.For<IChannel>();
        var references = NewReferences();
        var sut = CreateSut(channel, references, handledTypes: []);

        await sut.PublishAsync(CreateEnvelope());
        await sut.PublishAsync(CreateEnvelope());

        await AssertDeclaredNoQueueAsync(channel);
        await channel.Received(1).ExchangeDeclareAsync(
            references.ExchangeName, ExchangeType.Topic, Configuration.Durable, Configuration.AutoDelete,
            cancellationToken: Arg.Any<CancellationToken>());
        await channel.DidNotReceive().ExchangeDeclareAsync(
            Arg.Is<string>(exchange => exchange != references.ExchangeName), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_should_declare_the_exchange_only_on_a_host_that_only_publishes()
    {
        var channel = Substitute.For<IChannel>();
        var sut = CreateSut(channel, NewReferences(), handledTypes: [typeof(FakeSagaStarter)], publishOnly: true);

        await sut.PublishAsync(CreateEnvelope());

        await AssertDeclaredNoQueueAsync(channel);
    }

    [Fact]
    public async Task PublishAsync_should_declare_the_queue_of_a_message_the_host_handles()
    {
        var channel = Substitute.For<IChannel>();
        var references = NewReferences();
        var sut = CreateSut(channel, references, handledTypes: [typeof(FakeSagaStarter)]);

        await sut.PublishAsync(CreateEnvelope());

        foreach (var queue in new[] { references.QueueName, references.RetryQueueName, references.DeadLetterQueue })
            await channel.Received(1).QueueDeclareAsync(
                queue, Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<IDictionary<string, object?>>(),
                cancellationToken: Arg.Any<CancellationToken>());
    }

    private static async Task AssertDeclaredNoQueueAsync(IChannel channel)
    {
        await channel.DidNotReceiveWithAnyArgs().QueueDeclareAsync(default!, default, default, default, default, default, default, default);
        await channel.DidNotReceiveWithAnyArgs().QueueBindAsync(default!, default!, default!, default, default, default);
    }

    private static RabbitPublisher CreateSut(IChannel channel, QueueReferences references, IEnumerable<Type> handledTypes, bool publishOnly = false)
    {
        var channelFactory = Substitute.For<IChannelFactory>();
        channelFactory.GetPublishChannelAsync(Arg.Any<CancellationToken>()).Returns(channel);

        var queueReferenceFactory = Substitute.For<IQueueReferenceFactory>();
        queueReferenceFactory.Create(Arg.Any<MessageEnvelope>()).Returns(references);

        var sagaDescriptorsResolver = Substitute.For<ISagaDescriptorsResolver>();
        sagaDescriptorsResolver.GetRegisteredMessageTypes().Returns(handledTypes);

        var systemInfo = Substitute.For<ISystemInfo>();
        systemInfo.PublishOnly.Returns(publishOnly);

        return new RabbitPublisher(
            queueReferenceFactory,
            Configuration,
            channelFactory,
            Substitute.For<ILogger<RabbitPublisher>>(),
            new JsonSerializer(),
            sagaDescriptorsResolver,
            systemInfo);
    }

    // what was declared is remembered for the process, by name: every test has names of its own
    private static QueueReferences NewReferences()
    {
        var exchangeName = Guid.NewGuid().ToString("N");
        return new QueueReferences(exchangeName, $"{exchangeName}.api", exchangeName, $"{exchangeName}.dead", $"{exchangeName}.dead.api");
    }

    private static MessageEnvelope CreateEnvelope()
    {
        var saga = Substitute.For<ISagaInstance>();
        saga.CorrelationId.Returns(Guid.NewGuid().ToString());
        saga.TriggerMessageId.Returns(Guid.NewGuid().ToString());
        saga.InstanceId.Returns(Guid.NewGuid().ToString());
        return MessageEnvelope.Create(new FakeSagaStarter(), saga);
    }
}
