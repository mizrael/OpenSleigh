using System;
using FluentAssertions;
using NSubstitute;
using OpenSleigh.Core.Messaging;
using Xunit;

namespace OpenSleigh.Transport.Kafka.Tests.Unit
{
    public class ConsumerBuilderFactoryTests
    {
        [Fact]
        public void ctor_should_throw_when_input_null()
        {
            var groupIdFactory = NSubstitute.Substitute.For<IGroupIdFactory>();
            var config = new KafkaConfiguration("lorem");
            Assert.Throws<ArgumentNullException>( () => new ConsumerBuilderFactory(null, config));
            Assert.Throws<ArgumentNullException>( () => new ConsumerBuilderFactory(groupIdFactory, null));
        }
        
        [Fact]
        public void Create_should_return_valid_instance()
        {
            var groupIdFactory = NSubstitute.Substitute.For<IGroupIdFactory>();
            groupIdFactory.Create<IMessage>().Returns("ipsum");
            var config = new KafkaConfiguration("lorem");
            var sut = new ConsumerBuilderFactory(groupIdFactory, config);
            var result = sut.Create<IMessage, string,  ReadOnlyMemory<byte>>();
            result.Should().NotBeNull();
     
        }
    }
}