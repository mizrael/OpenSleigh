using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenSleigh.Persistence.InMemory.Messaging;
using Xunit;

namespace OpenSleigh.Persistence.InMemory.Tests.Unit
{
    public class InMemoryPublisherTests
    {
        [Fact]
        public void ctor_should_fail_when_arguments_null()
        {
            var channelFactory = NSubstitute.Substitute.For<IChannelFactory>();
            var logger = NSubstitute.Substitute.For<ILogger<InMemoryPublisher>>();
            Assert.Throws<ArgumentNullException>(() => new InMemoryPublisher(null, logger));
            Assert.Throws<ArgumentNullException>(() => new InMemoryPublisher(channelFactory, null));
        }
        
        [Fact]
        public async Task PublishAsync_should_throw_ArgumentNullException_when_message_null()
        {
            var channelFactory = NSubstitute.Substitute.For<IChannelFactory>();
            var logger = NSubstitute.Substitute.For<ILogger<InMemoryPublisher>>();
            
            var sut = new InMemoryPublisher(channelFactory, logger);
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.PublishAsync(null));
        }

        [Fact]
        public async Task PublishAsync_should_write_message_to_proper_channel()
        {
            var message = DummyMessage.New();
            
            var channelFactory = NSubstitute.Substitute.For<IChannelFactory>();
            var writer = NSubstitute.Substitute.For<ChannelWriter<DummyMessage>>();
            channelFactory.GetWriter<DummyMessage>()
                        .Returns(writer);

            var logger = NSubstitute.Substitute.For<ILogger<InMemoryPublisher>>();
            
            var sut = new InMemoryPublisher(channelFactory, logger);

            await sut.PublishAsync(message);

            await writer.Received(1)
                .WriteAsync(message, Arg.Any<CancellationToken>());
        }
    }
}
