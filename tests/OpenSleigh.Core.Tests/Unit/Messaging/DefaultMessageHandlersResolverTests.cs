using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Tests.Sagas;
using Xunit;
using FluentAssertions;
using System.Collections.Generic;
using System;
using NSubstitute;

namespace OpenSleigh.Core.Tests.Unit.Messaging
{
    public class DefaultMessageHandlersResolverTests
    {
        [Fact]
        public void Resolve_should_return_empty_collection_when_no_handlers_registered_for_message()
        {
            var sp = NSubstitute.Substitute.For<IServiceProvider>();

            var sut = new DefaultMessageHandlersResolver(sp);
            var handlers = sut.Resolve<StartDummySaga>();
            handlers.Should().NotBeNull().And.BeEmpty();
        }

        [Fact]
        public void Resolve_should_return_only_handlers_registered_for_message()
        {
            var messageHandlers = new[]
            {
                NSubstitute.Substitute.For<IHandleMessage<StartDummySaga>>(),
                NSubstitute.Substitute.For<IHandleMessage<StartDummySaga>>(),                                
            };

            var invalidHandlers = new[]
            {
                NSubstitute.Substitute.For<IHandleMessage<DummyEvent>>(),
                NSubstitute.Substitute.For<IHandleMessage<DummyEvent>>(),
            };

            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            sp.GetService(typeof(IEnumerable<IHandleMessage<StartDummySaga>>))
                .Returns(messageHandlers);

            sp.GetService(typeof(IEnumerable<IHandleMessage<DummyEvent>>))
                .Returns(invalidHandlers);

            var sut = new DefaultMessageHandlersResolver(sp);
            var handlers = sut.Resolve<StartDummySaga>();
            handlers.Should().NotBeNullOrEmpty()
                .And.BeEquivalentTo(messageHandlers);
        }

        [Fact]
        public void Resolve_should_not_return_sagas()
        {
            var state = new DummySagaState(Guid.NewGuid());

            var expectedHandler = NSubstitute.Substitute.For<IHandleMessage<StartDummySaga>>();
            var messageHandlers = new[]
            {
                expectedHandler,
                NSubstitute.Substitute.ForPartsOf<DummySaga>(state),
            };

            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            sp.GetService(typeof(IEnumerable<IHandleMessage<StartDummySaga>>))
                .Returns(messageHandlers);

            var sut = new DefaultMessageHandlersResolver(sp);
            var handlers = sut.Resolve<StartDummySaga>();
            handlers.Should().NotBeNullOrEmpty()
                .And.HaveCount(1)
                .And.Contain(expectedHandler);
        }
    }
}
