using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NSubstitute;
using Xunit;

namespace OpenSleigh.Persistence.InMemory.Tests.Unit
{
    public class InMemoryPublisherTests
    {
        [Fact]
        public async Task PublishAsync_should_throw_ArgumentNullException_when_message_null()
        {
            var channelFactory = NSubstitute.Substitute.For<IChannelFactory>();

            var sut = new InMemoryPublisher(channelFactory);
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
        
            var sut = new InMemoryPublisher(channelFactory);

            await sut.PublishAsync(message);

            await writer.Received(1)
                .WriteAsync(message, Arg.Any<CancellationToken>());
        }
    }
}
