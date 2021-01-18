using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using OpenSleigh.Core.Tests.Sagas;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit
{
    public class SagaTypeResolverTests
    {
        [Fact]
        public void Register_should_return_false_if_saga_handles_no_messages()
        {
            var sut = new SagaTypeResolver();
            var result = sut.Register<EmptySaga, DummySagaState>();
            result.Should().BeFalse();
        }

        [Fact]
        public void Register_should_return_true_if_saga_handles_messages()
        {
            var sut = new SagaTypeResolver();
            var result = sut.Register<DummySaga, DummySagaState>();
            result.Should().BeTrue();
        }

        [Fact]
        public void Register_should_throw_if_message_handler_already_registered()
        {
            var sut = new SagaTypeResolver();
            sut.Register<DummySaga, DummySagaState>();
            Assert.Throws<TypeAccessException>(() => sut.Register<DummySaga, DummySagaState>());
        }

        [Fact]
        public void Resolve_should_return_null_when_no_saga_registered_for_message()
        {
            var sut = new SagaTypeResolver();
            var result = sut.Resolve<StartDummySaga>();
            result.Should().NotBeNull();
            result.sagaType.Should().BeNull();
            result.sagaStateType.Should().BeNull();
        }

        [Fact]
        public void Resolve_should_return_registered_saga()
        {
            var sut = new SagaTypeResolver();
            
            sut.Register<DummySaga, DummySagaState>();
            
            var result = sut.Resolve<StartDummySaga>();
            result.Should().NotBeNull();
            result.sagaType.Should().Be(typeof(DummySaga));
            result.sagaStateType.Should().Be(typeof(DummySagaState));
        }
    }
}
