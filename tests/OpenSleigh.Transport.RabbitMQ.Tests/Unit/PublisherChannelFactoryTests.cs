using System;
using System.Runtime.InteropServices;
using FluentAssertions;
using NSubstitute;
using RabbitMQ.Client;
using Xunit;

namespace OpenSleigh.Transport.RabbitMQ.Tests.Unit
{
    public class PublisherChannelFactoryTests
    {
        [Fact]
        public void ctor_should_throw_when_argument_null()
        {
            var pool = NSubstitute.Substitute.For<IPublisherChannelContextPool>();
            var factory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
            Assert.Throws<ArgumentNullException>(() => new PublisherChannelFactory(null, factory));
            Assert.Throws<ArgumentNullException>(() => new PublisherChannelFactory(pool, null));
        }

        [Fact]
        public void Create_should_throw_when_argument_null()
        {
            var pool = NSubstitute.Substitute.For<IPublisherChannelContextPool>();
            var factory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
          
            var sut = new PublisherChannelFactory(pool, factory);

            Assert.Throws<ArgumentNullException>(() => sut.Create(null));
        }

        [Fact]
        public void Create_should_return_valid_context()
        {
            var channel = NSubstitute.Substitute.For<IModel>();
            var references = new QueueReferences("exchange", "queue", "deadletterExch", "deadLetterQ"); 
            
            var pool = NSubstitute.Substitute.For<IPublisherChannelContextPool>();

            var ctx = new PublisherChannelContext(channel, references, pool);
            pool.Get(references)
                .Returns(ctx);
                
            var factory = NSubstitute.Substitute.For<IQueueReferenceFactory>();
            
            factory.Create(null)
                .ReturnsForAnyArgs(references);
            
            var sut = new PublisherChannelFactory(pool, factory);

            var message = DummyMessage.New();
            var result = sut.Create(message);
            result.Should().NotBeNull();
            result.Channel.Should().Be(channel);
            result.QueueReferences.Should().Be(references);
        }
    }
}