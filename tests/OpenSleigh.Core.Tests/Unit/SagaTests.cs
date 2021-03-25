using System;
using FluentAssertions;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Tests.Sagas;
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

        //TODO: add tests for Publish
    }
}
