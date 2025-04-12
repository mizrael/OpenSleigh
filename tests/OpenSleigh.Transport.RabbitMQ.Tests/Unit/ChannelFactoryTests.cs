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
    public async Task Get_should_throw_when_input_null()
    {
        var connection = NSubstitute.Substitute.For<IBusConnection>();
        var config = new RabbitConfiguration("localhost", "ipsum", "dolor");
        var logger = NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<ChannelFactory>>();
        var sut = new ChannelFactory(connection, config, logger);

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.GetAsync(null, cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task Get_should_create_exchanges()
    {
        var queueReferences = new QueueReferences("foo", "bar", "baz", "qux"); 
        
        var channel = NSubstitute.Substitute.For<IChannel>();
        var connection = NSubstitute.Substitute.For<IBusConnection>();
        connection.CreateChannelAsync().Returns(channel);

        var config = new RabbitConfiguration("localhost", "ipsum", "dolor");
        var logger = NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<ChannelFactory>>();
        
        var sut = new ChannelFactory(connection, config, logger);
        await sut.GetAsync(queueReferences); 
        
        channel.Received(1).ExchangeDeclareAsync(exchange: queueReferences.RetryExchangeName, type: ExchangeType.Topic);
        channel.Received(1).ExchangeDeclareAsync(exchange: queueReferences.ExchangeName, type: ExchangeType.Topic);
        channel.Received(1).ExchangeDeclareAsync(exchange: queueReferences.DeadLetterExchangeName, type: ExchangeType.Topic);
    }

    [Fact]
    public async Task Get_should_create_dead_letter_queue()
    {
        var queueReferences = new QueueReferences("foo", "bar", "baz", "qux");

        var channel = NSubstitute.Substitute.For<IChannel>();
        var connection = NSubstitute.Substitute.For<IBusConnection>();
        connection.CreateChannelAsync().Returns(channel);

        var config = new RabbitConfiguration("localhost", "ipsum", "dolor");
        var logger = NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<ChannelFactory>>();

        var sut = new ChannelFactory(connection, config, logger);
        await sut.GetAsync(queueReferences);

        channel.Received(1).QueueDeclareAsync(queue: queueReferences.DeadLetterQueue,
             durable: true,
             exclusive: false,
             autoDelete: false,
             arguments: null);
        channel.Received(1).QueueBindAsync(queueReferences.DeadLetterQueue,
                          queueReferences.DeadLetterExchangeName,
                          routingKey: queueReferences.DeadLetterQueue,
                          arguments: null);
    }

    [Fact]
    public async Task Get_should_create_retry_queue()
    {
        var queueReferences = new QueueReferences("foo", "bar", "baz", "qux");

        var channel = NSubstitute.Substitute.For<IChannel>();
        var connection = NSubstitute.Substitute.For<IBusConnection>();
        connection.CreateChannelAsync().Returns(channel);

        var config = new RabbitConfiguration("localhost", "ipsum", "dolor", retryDelay: TimeSpan.FromSeconds(1));
        var logger = NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<ChannelFactory>>();

        var sut = new ChannelFactory(connection, config, logger);
        await sut.GetAsync(queueReferences);

        channel.Received(1).QueueDeclareAsync(queue: queueReferences.RetryQueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: Arg.Is<Dictionary<string, object>>(d => 
                d.ContainsKey(Headers.XMessageTTL) && (int)d[Headers.XMessageTTL] == (int)config.RetryDelay.TotalMilliseconds &&
                d.ContainsKey(Headers.XDeadLetterExchange) && d[Headers.XDeadLetterExchange] == queueReferences.ExchangeName &&
                d.ContainsKey(Headers.XDeadLetterRoutingKey) && d[Headers.XDeadLetterRoutingKey] == queueReferences.QueueName));
        channel.Received(1).QueueBindAsync(queue: queueReferences.RetryQueueName,
            exchange: queueReferences.RetryExchangeName,
            routingKey: queueReferences.RoutingKey,
            arguments: null);
    }

    [Fact]
    public async Task Get_should_create_queue()
    {
        var queueReferences = new QueueReferences("foo", "bar", "baz", "qux");

        var channel = NSubstitute.Substitute.For<IChannel>();
        var connection = NSubstitute.Substitute.For<IBusConnection>();
        connection.CreateChannelAsync().Returns(channel);

        var config = new RabbitConfiguration("localhost", "ipsum", "dolor", retryDelay: TimeSpan.FromSeconds(1));
        var logger = NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<ChannelFactory>>();

        var sut = new ChannelFactory(connection, config, logger);
        await sut.GetAsync(queueReferences);

        channel.Received(1).QueueDeclareAsync(queue: queueReferences.QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: Arg.Is<Dictionary<string, object>>(d =>
                d.ContainsKey(Headers.XDeadLetterExchange) && d[Headers.XDeadLetterExchange] == queueReferences.DeadLetterExchangeName &&
                d.ContainsKey(Headers.XDeadLetterRoutingKey) && d[Headers.XDeadLetterRoutingKey] == queueReferences.DeadLetterQueue));
        channel.Received(1).QueueBindAsync(queue: queueReferences.QueueName,
            exchange: queueReferences.ExchangeName,
            routingKey: queueReferences.RoutingKey,
            arguments: null);
    }

    [Fact]
    public async Task Dispose_should_dispose_cached_channels()
    {
        var queueReferences = new QueueReferences("foo", "bar", "baz", "qux");

        var channel = NSubstitute.Substitute.For<IChannel>();
        var connection = NSubstitute.Substitute.For<IBusConnection>();
        connection.CreateChannelAsync().Returns(channel);

        var config = new RabbitConfiguration("localhost", "ipsum", "dolor", retryDelay: TimeSpan.FromSeconds(1));
        var logger = NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<ChannelFactory>>();

        var sut = new ChannelFactory(connection, config, logger);
        sut.GetAsync(queueReferences);

        await sut.DisposeAsync();

        channel.Received(1).Dispose();
    }
}
