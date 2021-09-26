using System;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Tests.Sagas;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit
{
    public class DefaultSagaFactoryTests
    {
        [Fact]
        public void ctor_should_throw_when_input_null()
        {
            Assert.Throws<ArgumentNullException>(() => new DefaultSagaFactory<DummySaga, DummySagaState>(null));
        }
        
        [Fact]
        public void Create_should_throw_when_input_null()
        {
            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            var sut = new DefaultSagaFactory<DummySaga, DummySagaState>(sp);
            Assert.Throws<ArgumentNullException>(() => sut.Create(null));
        }

        [Fact]
        public void Create_should_create_valid_instance()
        {
            var state = new DummySagaState(Guid.NewGuid());
            var expectedSaga = new DummySaga(state);
            
            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            sp.GetService(typeof(DummySaga))
                .Returns(expectedSaga);

            var bus = NSubstitute.Substitute.For<IMessageBus>();
            sp.GetService(typeof(IMessageBus))
                .Returns(bus);

            var scope = NSubstitute.Substitute.For<IServiceScope>();
            scope.ServiceProvider.Returns(sp);

            var factory = NSubstitute.Substitute.For<IServiceScopeFactory>();
            factory.CreateScope().Returns(scope);
            
            sp.GetService(typeof(IServiceScopeFactory))
                .Returns(factory);

            var sut = new DefaultSagaFactory<DummySaga, DummySagaState>(sp);
            var saga = sut.Create(state);
            saga.Should().NotBeNull();
            saga.State.Should().Be(state);
        }
    }
}
