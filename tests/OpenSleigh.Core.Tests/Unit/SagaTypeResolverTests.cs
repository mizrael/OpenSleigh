using System;
using System.Linq;
using FluentAssertions;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Tests.Sagas;
using OpenSleigh.Core.Tests.Unit.Utils;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit
{
    public class SagaTypeResolverTests
    {
        [Fact]
        public void Register_should_return_false_if_saga_handles_no_messages()
        {
            var typeResolver = NSubstitute.Substitute.For<ITypeResolver>();
            var sut = new SagaTypeResolver(typeResolver);
            var result = sut.Register<EmptySaga, SagaState>();
            result.Should().BeFalse();
        }

        [Fact]
        public void Register_should_return_true_if_saga_handles_messages()
        {
            var typeResolver = NSubstitute.Substitute.For<ITypeResolver>();
            var sut = new SagaTypeResolver(typeResolver);
            var result = sut.Register<DummySaga, DummySagaState>();
            result.Should().BeTrue();
        }

        [Fact]
        public void Register_should_throw_if_message_handler_already_registered()
        {
            var typeResolver = NSubstitute.Substitute.For<ITypeResolver>();
            var sut = new SagaTypeResolver(typeResolver);
            sut.Register<DummySaga, DummySagaState>();
            Assert.Throws<TypeAccessException>(() => sut.Register<DummySaga, DummySagaState>());
        }

        [Fact]
        public void Resolve_should_return_empty_collection_when_no_saga_registered_for_message()
        {
            var typeResolver = NSubstitute.Substitute.For<ITypeResolver>();
            var sut = new SagaTypeResolver(typeResolver);

            sut.Register<DummySaga, DummySagaState>();

            var result = sut.Resolve<UnhandledMessage>();
            result.Should().NotBeNull().And.BeEmpty();
        }

        [Fact]
        public void Resolve_should_return_only_registered_saga_for_message()
        {
            var typeResolver = NSubstitute.Substitute.For<ITypeResolver>();
            var sut = new SagaTypeResolver(typeResolver);

            sut.Register<DummySaga, DummySagaState>();
            
            var result = sut.Resolve<StartDummySaga>();
            result.Should().NotBeNull().And.HaveCount(1);
            
            result.First().sagaType.Should().Be(typeof(DummySaga));
            result.First().sagaStateType.Should().Be(typeof(DummySagaState));
        }
    }
}
