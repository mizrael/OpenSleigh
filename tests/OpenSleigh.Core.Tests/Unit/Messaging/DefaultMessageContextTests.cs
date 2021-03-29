using System;
using FluentAssertions;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Tests.Sagas;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit.Messaging
{
    public class DefaultMessageContextTests
    {
        [Fact]
        public void ctor_should_throw_when_input_null()
        {
            var message = StartDummySaga.New();
            var sysInfo = SystemInfo.New();
            Assert.Throws<ArgumentNullException>(() => new DefaultMessageContext<StartDummySaga>(null, sysInfo));
            Assert.Throws<ArgumentNullException>(() => new DefaultMessageContext<StartDummySaga>(message, null));
        }

        [Fact]
        public void ctor_should_return_valid_instance()
        {
            var message = StartDummySaga.New();
            var sysInfo = SystemInfo.New();
            var sut = new DefaultMessageContext<StartDummySaga>(message, sysInfo);
            sut.Message.Should().Be(message);
            sut.SystemInfo.Should().Be(sysInfo);
        }
    }
}