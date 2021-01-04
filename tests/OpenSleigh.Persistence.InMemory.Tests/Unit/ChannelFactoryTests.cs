using System;
using System.Threading.Channels;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace OpenSleigh.Persistence.InMemory.Tests.Unit
{
    public class ChannelFactoryTests
    {
        [Fact]
        public void GetWriter_should_return_null_when_channel_not_registered()
        {
            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            var sut = new ChannelFactory(sp);
            var result = sut.GetWriter<DummyMessage>();
            result.Should().BeNull();
        }

        [Fact]
        public void GetWriter_should_return_writer_when_channel_registered()
        {
            var expectedWriter = NSubstitute.Substitute.For<ChannelWriter<DummyMessage>>(); 
            
            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            sp.GetService(typeof(ChannelWriter<DummyMessage>))
                .Returns(expectedWriter);
            
            var sut = new ChannelFactory(sp);
            var result = sut.GetWriter<DummyMessage>();
            result.Should().Be(expectedWriter);
        }
    }
}