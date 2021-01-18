using System;
using OpenSleigh.Core.Exceptions;
using OpenSleigh.Core.Tests.Sagas;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit
{
    public class DefaultSagaStateFactoryTests
    {
        [Fact]
        public void ctor_should_throw_when_input_null()
        {
            Assert.Throws<ArgumentNullException>(() => new DefaultSagaStateFactory<DummySagaState>(null));
        }

        [Fact]
        public void Create_should_throw_when_input_null()
        {
            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            var sut = new DefaultSagaStateFactory<DummySagaState>(sp);
            Assert.Throws<ArgumentNullException>(() => sut.Create(null));
        }

        [Fact]
        public void Create_should_throw_when_no_factory_registered_for_input_message()
        {
            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            var sut = new DefaultSagaStateFactory<DummySagaState>(sp);

            var message = StartDummySaga.New();
            var ex = Assert.Throws<StateCreationException>(() => sut.Create(message));
            ex.Message.Contains($"no state factory registered for message type '{message.GetType().FullName}'");
        }
    }
}
