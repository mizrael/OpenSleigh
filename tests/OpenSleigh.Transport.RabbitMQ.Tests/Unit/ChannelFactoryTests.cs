using Microsoft.Extensions.Logging;
using NSubstitute;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.RabbitMQ.Tests.Unit;

public class ChannelFactoryTests
{
    [Fact]
    public async Task GetPublishChannel_should_create_a_channel_and_report_the_messages_the_broker_returns()
    {
        var channel = Substitute.For<IChannel>();
        channel.IsOpen.Returns(true);

        var connection = Substitute.For<IBusConnection>();
        connection.CreateChannelAsync(Arg.Any<CancellationToken>()).Returns(channel);
        var warnings = new List<string>();
        var logger = Substitute.For<ILogger<ChannelFactory>>();
        logger.IsEnabled(LogLevel.Warning).Returns(true);
        logger.WhenForAnyArgs(l => l.Log<object>(default, default, default!, default, default!))
              .Do(call =>
              {
                  if (call.Arg<LogLevel>() == LogLevel.Warning)
                      warnings.Add(call.Args()[2]!.ToString()!);
              });

        var sut = new ChannelFactory(connection, logger);
        var result = await sut.GetPublishChannelAsync(CancellationToken.None);

        Assert.Same(channel, result);
        await connection.Received(1).CreateChannelAsync(Arg.Any<CancellationToken>());

        var properties = new BasicProperties
        {
            MessageId = "message-1",
            Headers = new Dictionary<string, object?> { ["MessageType"] = Encoding.UTF8.GetBytes("Some.Message") }
        };
        var returned = new global::RabbitMQ.Client.Events.BasicReturnEventArgs(
            312, "NO_ROUTE", "the-exchange", "bar", properties, ReadOnlyMemory<byte>.Empty, CancellationToken.None);
        channel.BasicReturnAsync += Raise.Event<global::RabbitMQ.Client.Events.AsyncEventHandler<global::RabbitMQ.Client.Events.BasicReturnEventArgs>>(channel, returned);

        var warning = Assert.Single(warnings);
        Assert.Contains("message-1", warning);
        Assert.Contains("Some.Message", warning);
        Assert.Contains("the-exchange", warning);
        Assert.Contains("NO_ROUTE", warning);
    }

    [Fact]
    public async Task GetPublishChannel_should_hand_the_messages_the_broker_returns_to_their_handler()
    {
        var channel = Substitute.For<IChannel>();
        channel.IsOpen.Returns(true);
        var connection = Substitute.For<IBusConnection>();
        connection.CreateChannelAsync(Arg.Any<CancellationToken>()).Returns(channel);
        var returnedMessages = Substitute.For<IReturnedMessageHandler>();
        var logger = Substitute.For<ILogger<ChannelFactory>>();

        var sut = new ChannelFactory(connection, logger, returnedMessages);
        await sut.GetPublishChannelAsync(CancellationToken.None);

        var returned = new global::RabbitMQ.Client.Events.BasicReturnEventArgs(
            312, "NO_ROUTE", "the-exchange", "bar", new BasicProperties(), ReadOnlyMemory<byte>.Empty, CancellationToken.None);
        channel.BasicReturnAsync += Raise.Event<global::RabbitMQ.Client.Events.AsyncEventHandler<global::RabbitMQ.Client.Events.BasicReturnEventArgs>>(channel, returned);

        returnedMessages.Received(1).Accept(returned);
        Assert.Empty(logger.ReceivedCalls());
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

        channel.Received(1).ExchangeDeclareAsync(
            exchange: queueReferences.RetryExchangeName, 
            type: ExchangeType.Topic, 
            durable: rabbitCfg.Durable,            
            autoDelete: rabbitCfg.AutoDelete,
            cancellationToken: Arg.Any<CancellationToken>());
        channel.Received(1).ExchangeDeclareAsync(
            exchange: queueReferences.ExchangeName, 
            type: ExchangeType.Topic,
            durable: rabbitCfg.Durable,
            autoDelete: rabbitCfg.AutoDelete,
            cancellationToken: Arg.Any<CancellationToken>());
        channel.Received(1).ExchangeDeclareAsync(
            exchange: queueReferences.DeadLetterExchangeName,
            type: ExchangeType.Topic,
            durable: rabbitCfg.Durable,
            autoDelete: rabbitCfg.AutoDelete,
            cancellationToken: Arg.Any<CancellationToken>());

        channel.Received(1).QueueDeclareAsync(queue: queueReferences.DeadLetterQueue,
             durable: rabbitCfg.Durable,
             exclusive: false,
             autoDelete: rabbitCfg.AutoDelete,
             arguments: null,
             cancellationToken: Arg.Any<CancellationToken>());

        channel.Received(1).QueueDeclareAsync(queue: queueReferences.RetryQueueName,
                durable: rabbitCfg.Durable,
                 exclusive: false,
                 autoDelete: rabbitCfg.AutoDelete,
                arguments: Arg.Is<Dictionary<string, object?>>(d =>
                    d.ContainsKey(Headers.XMessageTTL) && (int)d[Headers.XMessageTTL]! == (int)rabbitCfg.RetryDelay.TotalMilliseconds &&
                    d.ContainsKey(Headers.XDeadLetterExchange) && (string)d[Headers.XDeadLetterExchange]! == queueReferences.ExchangeName &&
                    d.ContainsKey(Headers.XDeadLetterRoutingKey) && (string)d[Headers.XDeadLetterRoutingKey]! == queueReferences.QueueName),
                cancellationToken: Arg.Any<CancellationToken>());
        channel.Received(1).QueueBindAsync(queue: queueReferences.RetryQueueName,
                exchange: queueReferences.RetryExchangeName,
                routingKey: queueReferences.RetryQueueName,
                arguments: null,
                cancellationToken: Arg.Any<CancellationToken>());

        channel.Received(1).QueueDeclareAsync(queue: queueReferences.QueueName,
               durable: rabbitCfg.Durable,
                 exclusive: false,
                 autoDelete: rabbitCfg.AutoDelete,
                arguments: Arg.Is<Dictionary<string, object?>>(d =>
                    d.ContainsKey(Headers.XDeadLetterExchange) && (string)d[Headers.XDeadLetterExchange]! == queueReferences.DeadLetterExchangeName &&
                    d.ContainsKey(Headers.XDeadLetterRoutingKey) && (string)d[Headers.XDeadLetterRoutingKey]! == queueReferences.DeadLetterQueue),
                cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_closed_channel_should_be_left_for_the_client_to_recover_while_there_is_no_connection()
    {
        var channel = Substitute.For<IChannel>();
        channel.IsOpen.Returns(true);
        var connection = Substitute.For<IBusConnection>();
        connection.CreateChannelAsync(Arg.Any<CancellationToken>())
                  .Returns(Task.FromResult(channel), Task.FromException<IChannel>(new InvalidOperationException("the connection is closed")));
        var sut = new ChannelFactory(connection);
        await sut.GetPublishChannelAsync();

        channel.IsOpen.Returns(false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetPublishChannelAsync().AsTask());
        channel.DidNotReceive().Dispose();

        channel.IsOpen.Returns(true);
        Assert.Same(channel, await sut.GetPublishChannelAsync());
        await connection.Received(2).CreateChannelAsync(Arg.Any<CancellationToken>());
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
