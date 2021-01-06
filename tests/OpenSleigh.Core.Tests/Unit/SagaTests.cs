using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using OpenSleigh.Core.Messaging;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit
{
    public class SagaTests
    {
        [Fact]
        public void SetState_should_throw_when_input_null()
        {
            var sut = new DummySaga();
            Assert.Throws<ArgumentNullException>(() => sut.SetState(null));
        }

        [Fact]
        public void SetState_should_set_state()
        {
            var sut = new DummySaga();

            var state = new DummySagaState(Guid.NewGuid());
            sut.SetState(state);
            sut.State.Should().Be(state);
        }

        [Fact]
        public void SetBus_should_throw_when_input_null()
        {
            var sut = new DummySaga();
            Assert.Throws<ArgumentNullException>(() => sut.SetBus(null));
        }

        [Fact]
        public void SetBus_should_set_state()
        {
            var sut = new DummySaga();

            var bus = NSubstitute.Substitute.For<IMessageBus>();
            sut.SetBus(bus);
            sut.Bus.Should().Be(bus);
        }
    }
}
