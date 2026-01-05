using FluentAssertions;
using System;

namespace OpenSleigh.Transport.Kafka.Tests.Unit;

public class ConsumerBuilderFactoryTests
{
    [Fact]
    public void ctor_should_throw_when_input_null()
    {
        var sysInfo = NSubstitute.Substitute.For<ISystemInfo>();
        Assert.Throws<ArgumentNullException>( () => new ConsumerBuilderFactory(null!, sysInfo));
    }
    
    [Fact]
    public void Create_should_return_valid_instance()
    {
        var sysInfo = NSubstitute.Substitute.For<ISystemInfo>();
        var config = new KafkaConfiguration("lorem");
        var sut = new ConsumerBuilderFactory(config, sysInfo);
        var result = sut.Create<string, byte[]>();
        result.Should().NotBeNull();
 
    }
}