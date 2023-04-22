using NSubstitute;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using Xunit;

namespace OpenSleigh.Transport.RabbitMQ.Tests.Unit
{
    public class ChannelFactoryTests
    {
        [Fact]
        public void Get_should_throw_when_input_null()
        {
            var connection = NSubstitute.Substitute.For<IBusConnection>();
            var config = new RabbitConfiguration("localhost", "ipsum", "dolor");
            var logger = NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<ChannelFactory>>();
            var sut = new ChannelFactory(connection, config, logger);

            Assert.Throws<ArgumentNullException>(() => sut.Get(null));
        }

        [Fact]
        public void Get_should_create_exchanges()
        {
            var queueReferences = new QueueReferences("foo", "bar", "baz", "qux"); 
            
            var channel = NSubstitute.Substitute.For<IModel>();
            var connection = NSubstitute.Substitute.For<IBusConnection>();
            connection.CreateChannel().Returns(channel);

            var config = new RabbitConfiguration("localhost", "ipsum", "dolor");
            var logger = NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<ChannelFactory>>();
            
            var sut = new ChannelFactory(connection, config, logger);
            sut.Get(queueReferences); 
            
            channel.Received(1).ExchangeDeclare(exchange: queueReferences.RetryExchangeName, type: ExchangeType.Topic);
            channel.Received(1).ExchangeDeclare(exchange: queueReferences.ExchangeName, type: ExchangeType.Topic);
            channel.Received(1).ExchangeDeclare(exchange: queueReferences.DeadLetterExchangeName, type: ExchangeType.Topic);
        }

        [Fact]
        public void Get_should_create_dead_letter_queue()
        {
            var queueReferences = new QueueReferences("foo", "bar", "baz", "qux");

            var channel = NSubstitute.Substitute.For<IModel>();
            var connection = NSubstitute.Substitute.For<IBusConnection>();
            connection.CreateChannel().Returns(channel);

            var config = new RabbitConfiguration("localhost", "ipsum", "dolor");
            var logger = NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<ChannelFactory>>();

            var sut = new ChannelFactory(connection, config, logger);
            sut.Get(queueReferences);

            channel.Received(1).QueueDeclare(queue: queueReferences.DeadLetterQueue,
                 durable: true,
                 exclusive: false,
                 autoDelete: false,
                 arguments: null);
            channel.Received(1).QueueBind(queueReferences.DeadLetterQueue,
                              queueReferences.DeadLetterExchangeName,
                              routingKey: queueReferences.DeadLetterQueue,
                              arguments: null);
        }

        [Fact]
        public void Get_should_create_retry_queue()
        {
            var queueReferences = new QueueReferences("foo", "bar", "baz", "qux");

            var channel = NSubstitute.Substitute.For<IModel>();
            var connection = NSubstitute.Substitute.For<IBusConnection>();
            connection.CreateChannel().Returns(channel);

            var config = new RabbitConfiguration("localhost", "ipsum", "dolor", retryDelay: TimeSpan.FromSeconds(1));
            var logger = NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<ChannelFactory>>();

            var sut = new ChannelFactory(connection, config, logger);
            sut.Get(queueReferences);

            channel.Received(1).QueueDeclare(queue: queueReferences.RetryQueueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: Arg.Is<Dictionary<string, object>>(d => 
                    d.ContainsKey(Headers.XMessageTTL) && (int)d[Headers.XMessageTTL] == (int)config.RetryDelay.TotalMilliseconds &&
                    d.ContainsKey(Headers.XDeadLetterExchange) && d[Headers.XDeadLetterExchange] == queueReferences.ExchangeName &&
                    d.ContainsKey(Headers.XDeadLetterRoutingKey) && d[Headers.XDeadLetterRoutingKey] == queueReferences.QueueName));
            channel.Received(1).QueueBind(queue: queueReferences.RetryQueueName,
                exchange: queueReferences.RetryExchangeName,
                routingKey: queueReferences.RoutingKey,
                arguments: null);
        }

        [Fact]
        public void Get_should_create_queue()
        {
            var queueReferences = new QueueReferences("foo", "bar", "baz", "qux");

            var channel = NSubstitute.Substitute.For<IModel>();
            var connection = NSubstitute.Substitute.For<IBusConnection>();
            connection.CreateChannel().Returns(channel);

            var config = new RabbitConfiguration("localhost", "ipsum", "dolor", retryDelay: TimeSpan.FromSeconds(1));
            var logger = NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<ChannelFactory>>();

            var sut = new ChannelFactory(connection, config, logger);
            sut.Get(queueReferences);

            channel.Received(1).QueueDeclare(queue: queueReferences.QueueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: Arg.Is<Dictionary<string, object>>(d =>
                    d.ContainsKey(Headers.XDeadLetterExchange) && d[Headers.XDeadLetterExchange] == queueReferences.DeadLetterExchangeName &&
                    d.ContainsKey(Headers.XDeadLetterRoutingKey) && d[Headers.XDeadLetterRoutingKey] == queueReferences.DeadLetterQueue));
            channel.Received(1).QueueBind(queue: queueReferences.QueueName,
                exchange: queueReferences.ExchangeName,
                routingKey: queueReferences.RoutingKey,
                arguments: null);
        }

        [Fact]
        public void Dispose_should_dispose_cached_channels()
        {
            var queueReferences = new QueueReferences("foo", "bar", "baz", "qux");

            var channel = NSubstitute.Substitute.For<IModel>();
            var connection = NSubstitute.Substitute.For<IBusConnection>();
            connection.CreateChannel().Returns(channel);

            var config = new RabbitConfiguration("localhost", "ipsum", "dolor", retryDelay: TimeSpan.FromSeconds(1));
            var logger = NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<ChannelFactory>>();

            var sut = new ChannelFactory(connection, config, logger);
            sut.Get(queueReferences);

            sut.Dispose();

            channel.Received(1).Dispose();
        }
    }
}
