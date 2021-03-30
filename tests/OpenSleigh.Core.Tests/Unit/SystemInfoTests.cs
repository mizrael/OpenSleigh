using System;
using FluentAssertions;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit
{
    public class SystemInfoTests
    {
        [Fact]
        public void New_should_create_valid_instance()
        {
            var sut = SystemInfo.New();
            sut.Should().NotBeNull();
            sut.ClientId.Should().NotBeEmpty();
            sut.PublishOnly.Should().BeFalse();
            sut.ClientGroup.Should().Be(System.AppDomain.CurrentDomain.FriendlyName);
        }

        [Fact]
        public void ctor_should_throw_when_input_invalid()
        {
            Assert.Throws<ArgumentException>(() => new SystemInfo(Guid.NewGuid(), null));
            Assert.Throws<ArgumentException>(() => new SystemInfo(Guid.NewGuid(), ""));
            Assert.Throws<ArgumentException>(() => new SystemInfo(Guid.NewGuid(), "   "));
        }
    }
}

