using NSubstitute;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.RabbitMQ.Tests.Unit;

public class ChannelFactoryTests
{
    [Fact]
    public async Task GetPublishChannel_should_create_channel()
    {
        var channel = Substitute.For<IChannel>();
        channel.IsOpen.Returns(true);

        var connection = Substitute.For<IBusConnection>();
        connection.CreateChannelAsync(Arg.Any<CancellationToken>()).Returns(channel);

        var sut = new ChannelFactory(connection);
        var result = await sut.GetPublishChannelAsync(CancellationToken.None);

        Assert.Same(channel, result);
        await connection.Received(1).CreateChannelAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetConsumeChannel_should_create_channel()
    {
        var channel = Substitute.For<IChannel>();
        channel.IsOpen.Returns(true);

        var connection = Substitute.For<IBusConnection>();
        connection.CreateChannelAsync(Arg.Any<CancellationToken>()).Returns(channel);

        var sut = new ChannelFactory(connection);
        var result = await sut.GetConsumeChannelAsync(CancellationToken.None);

        Assert.Same(channel, result);
        await connection.Received(1).CreateChannelAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureTopology_should_create_exchanges_and_queues()
    {
        var queueReferences = new QueueReferences("foo", "bar", "baz", "qux");

        var channel = Substitute.For<IChannel>();
        channel.IsOpen.Returns(true);

        var connection = Substitute.For<IBusConnection>();
        connection.CreateChannelAsync(Arg.Any<CancellationToken>()).Returns(channel);

        var rabbitCfg = new RabbitConfiguration("localhost", "ipsum", "dolor", retryDelay: TimeSpan.FromSeconds(1));

        var sut = new ChannelFactory(connection);
        var publishChannel = await sut.GetPublishChannelAsync(CancellationToken.None);

        await publishChannel.EnsureTopologyAsync(queueReferences, rabbitCfg, CancellationToken.None);

        channel.Received(1).ExchangeDeclareAsync(exchange: queueReferences.RetryExchangeName, type: ExchangeType.Topic, cancellationToken: Arg.Any<CancellationToken>());
        channel.Received(1).ExchangeDeclareAsync(exchange: queueReferences.ExchangeName, type: ExchangeType.Topic, cancellationToken: Arg.Any<CancellationToken>());
        channel.Received(1).ExchangeDeclareAsync(exchange: queueReferences.DeadLetterExchangeName, type: ExchangeType.Topic, cancellationToken: Arg.Any<CancellationToken>());

        channel.Received(1).QueueDeclareAsync(queue: queueReferences.DeadLetterQueue,
             durable: true,
             exclusive: false,
             autoDelete: false,
             arguments: null,
             cancellationToken: Arg.Any<CancellationToken>());

        channel.Received(1).QueueDeclareAsync(queue: queueReferences.RetryQueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: Arg.Is<Dictionary<string, object?>>(d =>
                    d.ContainsKey(Headers.XMessageTTL) && (int)d[Headers.XMessageTTL]! == (int)rabbitCfg.RetryDelay.TotalMilliseconds &&
                    d.ContainsKey(Headers.XDeadLetterExchange) && (string)d[Headers.XDeadLetterExchange]! == queueReferences.ExchangeName &&
                    d.ContainsKey(Headers.XDeadLetterRoutingKey) && (string)d[Headers.XDeadLetterRoutingKey]! == queueReferences.RoutingKey),
                cancellationToken: Arg.Any<CancellationToken>());

        channel.Received(1).QueueDeclareAsync(queue: queueReferences.QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: Arg.Is<Dictionary<string, object?>>(d =>
                    d.ContainsKey(Headers.XDeadLetterExchange) && (string)d[Headers.XDeadLetterExchange]! == queueReferences.DeadLetterExchangeName &&
                    d.ContainsKey(Headers.XDeadLetterRoutingKey) && (string)d[Headers.XDeadLetterRoutingKey]! == queueReferences.DeadLetterQueue),
                cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dispose_should_dispose_cached_channels()
    {
        var channel = Substitute.For<IChannel>();
        channel.IsOpen.Returns(true);

        var connection = Substitute.For<IBusConnection>();
        // both publish + consume will be created with this same substitute instance
        connection.CreateChannelAsync(Arg.Any<CancellationToken>()).Returns(channel);

        var sut = new ChannelFactory(connection);
        await sut.GetPublishChannelAsync();
        await sut.GetConsumeChannelAsync();

        await sut.DisposeAsync();

        channel.Received().Dispose();
    }
}
