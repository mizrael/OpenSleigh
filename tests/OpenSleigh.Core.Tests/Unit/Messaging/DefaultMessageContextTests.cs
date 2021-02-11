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
            Assert.Throws<ArgumentNullException>(() => new DefaultMessageContext<StartDummySaga>(null));
        }

        [Fact]
        public void ctor_should_return_valid_instance()
        {
            var message = StartDummySaga.New();
            var sut = new DefaultMessageContext<StartDummySaga>(message);
            sut.Message.Should().Be(message);
        }
    }
}