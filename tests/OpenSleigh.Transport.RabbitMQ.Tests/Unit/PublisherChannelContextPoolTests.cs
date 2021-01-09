using System;
using RabbitMQ.Client;
using Xunit;
using FluentAssertions;
using NSubstitute;

namespace OpenSleigh.Transport.RabbitMQ.Tests.Unit
{
    public class PublisherChannelContextPoolTests
    {
        [Fact]
        public void ctor_should_throw_if_argument_null()
        {
            Assert.Throws<ArgumentNullException>(() => new PublisherChannelContextPool(null));
        }

        [Fact]
        public void Get_should_throw_if_argument_null()
        {
            var connection = NSubstitute.Substitute.For<IBusConnection>();
            var sut = new PublisherChannelContextPool(connection);
            
            Assert.Throws<ArgumentNullException>(() => sut.Get(null));
        }

        [Fact]
        public void Get_should_return_valid_channel()
        {
            var expectedChannel = NSubstitute.Substitute.For<IModel>();
            var connection = NSubstitute.Substitute.For<IBusConnection>();
            connection.CreateChannel()
                .Returns(expectedChannel);
            
            var sut = new PublisherChannelContextPool(connection);

            var references = new QueueReferences("lorem", "ipsum", "dolor", "amet");
            var result = sut.Get(references);

            result.Should().NotBeNull();
            result.QueueReferences.Should().Be(references);
            result.Channel.Should().Be(expectedChannel);

            expectedChannel.Received(1)
                .ExchangeDeclare(references.ExchangeName, type: ExchangeType.Fanout);
        }

        [Fact]
        public void Return_should_return_channel_to_the_pool()
        {
            var channel = NSubstitute.Substitute.For<IModel>();
            var references = new QueueReferences("lorem", "ipsum", "dolor", "amet");
            
            var connection = NSubstitute.Substitute.For<IBusConnection>();
            connection.CreateChannel()
                .Returns(channel);
            
            var sut = new PublisherChannelContextPool(connection);

            var ctx = new PublisherChannelContext(channel, references, sut);

            sut.Get(references);
            
            channel.Received(1)
                .ExchangeDeclare(references.ExchangeName, type: ExchangeType.Fanout);
            channel.ClearReceivedCalls();
            
            sut.Return(ctx);

            sut.Get(references);
            channel.DidNotReceive()
                .ExchangeDeclare(references.ExchangeName, type: ExchangeType.Fanout);
        }

        [Fact]
        public void Return_should_throw_if_arguments_null()
        {
            var connection = NSubstitute.Substitute.For<IBusConnection>();
            var sut = new PublisherChannelContextPool(connection);
            
            Assert.Throws<ArgumentNullException>(() => sut.Return(null));
        }

        [Fact]
        public void Dispose_should_close_open_channels()
        {
            var openChannel = NSubstitute.Substitute.For<IModel>();
            openChannel.IsOpen.Returns(true);

            var references = new QueueReferences("lorem", "ipsum", "dolor", "amet");

            var connection = NSubstitute.Substitute.For<IBusConnection>();
            
            var sut = new PublisherChannelContextPool(connection);

            var ctx = new PublisherChannelContext(openChannel, references, sut);

            sut.Return(ctx);
            sut.Dispose();
            
            openChannel.Received(1)
                .Close();
        }

        [Fact]
        public void Dispose_should_dispose_channels()
        {
            var openChannel = NSubstitute.Substitute.For<IModel>();
            
            var references = new QueueReferences("lorem", "ipsum", "dolor", "amet");

            var connection = NSubstitute.Substitute.For<IBusConnection>();

            var sut = new PublisherChannelContextPool(connection);

            var ctx = new PublisherChannelContext(openChannel, references, sut);

            sut.Return(ctx);
            sut.Dispose();

            openChannel.Received(1)
                .Dispose();
        }
    }
}
