using System;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace OpenSleigh.Transport.RabbitMQ.Tests.Unit
{
    public class PublisherChannelFactoryTests
    {
        [Fact]
        public void ctor_should_throw_when_argument_null()
        {
            var pool = NSubstitute.Substitute.For<IChannelPool>();
            var factory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
            Assert.Throws<ArgumentNullException>(() => new PublisherChannelFactory(null, factory));
            Assert.Throws<ArgumentNullException>(() => new PublisherChannelFactory(pool, null));
        }

        [Fact]
        public void Create_should_throw_when_argument_null()
        {
            var pool = NSubstitute.Substitute.For<IChannelPool>();
            var factory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
          
            var sut = new PublisherChannelFactory(pool, factory);

            Assert.Throws<ArgumentNullException>(() => sut.Create(null));
        }

        [Fact]
        public void Create_should_return_valid_context()
        {
            var pool = NSubstitute.Substitute.For<IChannelPool>();
            var factory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
            var references = new QueueReferences("exchange", "queue", "deadletterExch", "deadLetterQ");
            factory.Create(null)
                .ReturnsForAnyArgs(references);
            
            var sut = new PublisherChannelFactory(pool, factory);

            var message = DummyMessage.New();
            var result = sut.Create(message);
            result.Should().NotBeNull();
            result.Channel.Should().NotBeNull();
            result.QueueReferences.Should().Be(references);
        }
    }
}