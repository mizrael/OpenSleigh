using System;
using FluentAssertions;
using OpenSleigh.Core;
using OpenSleigh.Core.Tests.Sagas;
using Xunit;

namespace OpenSleigh.Transport.Kafka.Tests.Unit
{
    public class DefaultGroupIdFactoryTests
    {
        [Fact]
        public void ctor_should_throw_when_input_null()
        {
            Assert.Throws<ArgumentNullException>( () => new DefaultGroupIdFactory(null));
        }

        [Fact]
        public void Create_should_return_valid_value_for_messages()
        {
            var sysInfo = new SystemInfo(Guid.NewGuid(), "lorem");
            var sut = new DefaultGroupIdFactory(sysInfo);
            var result = sut.Create<DummyMessage>();
            result.Should().Be(typeof(DummyMessage).FullName);
        }
        
        [Fact]
        public void Create_should_return_valid_value_for_events()
        {
            var sysInfo = new SystemInfo(Guid.NewGuid(), "lorem");
            var sut = new DefaultGroupIdFactory(sysInfo);
            var result = sut.Create<DummyEvent>();
            result.Should().Be(typeof(DummyEvent).FullName + "." + sysInfo.ClientGroup);
        }
    }
}