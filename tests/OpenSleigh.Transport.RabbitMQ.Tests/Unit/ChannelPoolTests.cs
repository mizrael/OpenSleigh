using System;
using RabbitMQ.Client;
using Xunit;
using FluentAssertions;
using NSubstitute;

namespace OpenSleigh.Transport.RabbitMQ.Tests.Unit
{
    public class ChannelPoolTests
    {
        [Fact]
        public void ctor_should_throw_if_argument_null()
        {
            Assert.Throws<ArgumentNullException>(() => new ChannelPool(null));
        }

        [Fact]
        public void Get_should_throw_if_argument_null()
        {
            var connection = NSubstitute.Substitute.For<IBusConnection>();
            var sut = new ChannelPool(connection);
            
            Assert.Throws<ArgumentNullException>(() => sut.Get(null));
        }

        [Fact]
        public void Get_should_return_valid_channel()
        {
            var expectedChannel = NSubstitute.Substitute.For<IModel>();
            var connection = NSubstitute.Substitute.For<IBusConnection>();
            connection.CreateChannel()
                .Returns(expectedChannel);
            
            var sut = new ChannelPool(connection);

            var references = new QueueReferences("lorem", "ipsum", "dolor", "amet");
            var result = sut.Get(references);

            result.Should().Be(expectedChannel);

            expectedChannel.Received(1)
                .ExchangeDeclare(references.ExchangeName, type: ExchangeType.Fanout);
        }

        [Fact]
        public void Return_should_return_channel_to_the_pool()
        {
            var expectedChannel = NSubstitute.Substitute.For<IModel>();
            var connection = NSubstitute.Substitute.For<IBusConnection>();
            connection.CreateChannel()
                .Returns(expectedChannel);

            var sut = new ChannelPool(connection);

            var references = new QueueReferences("lorem", "ipsum", "dolor", "amet");
            var result = sut.Get(references);
            
            expectedChannel.Received(1)
                .ExchangeDeclare(references.ExchangeName, type: ExchangeType.Fanout);
            expectedChannel.ClearReceivedCalls();
            
            sut.Return(expectedChannel, references);

            result = sut.Get(references);
            expectedChannel.DidNotReceive()
                .ExchangeDeclare(references.ExchangeName, type: ExchangeType.Fanout);
        }

        [Fact]
        public void Return_should_throw_if_arguments_null()
        {
            var connection = NSubstitute.Substitute.For<IBusConnection>();
            var sut = new ChannelPool(connection);

            var channel = NSubstitute.Substitute.For<IModel>();
            var references = new QueueReferences("lorem", "ipsum", "dolor", "amet");
            
            Assert.Throws<ArgumentNullException>(() => sut.Return(null, references));
            Assert.Throws<ArgumentNullException>(() => sut.Return(channel, null));
        }
    }
}
