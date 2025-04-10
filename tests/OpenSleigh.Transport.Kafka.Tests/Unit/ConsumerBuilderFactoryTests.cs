using FluentAssertions;
using NSubstitute;
using System;
using Xunit;

namespace OpenSleigh.Transport.Kafka.Tests.Unit
{
    public class ConsumerBuilderFactoryTests
    {
        [Fact]
        public void ctor_should_throw_when_input_null()
        {
            Assert.Throws<ArgumentNullException>( () => new ConsumerBuilderFactory(null));
        }
        
        [Fact]
        public void Create_should_return_valid_instance()
        {
            var config = new KafkaConfiguration("lorem");
            var sut = new ConsumerBuilderFactory(config);
            var result = sut.Create<IMessage, string,  ReadOnlyMemory<byte>>();
            result.Should().NotBeNull();
     
        }
    }
}