using System;
using FluentAssertions;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Messaging;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit.DependencyInjection
{
    public class LambdaSagaStateFactoryTests
    {
        [Fact]
        public void Create_should_execute_factory()
        {
            var expectedState = new DummySagaState(Guid.NewGuid());
            var expectedMessage = StartDummySaga.New();
            
            Func<StartDummySaga, DummySagaState> factory = (msg) =>
            {
                msg.Should().Be(expectedMessage);
                return expectedState;
            };
            var sut = new LambdaSagaStateFactory<StartDummySaga, DummySagaState>(factory);
            var result = sut.Create(expectedMessage);
            result.Should().Be(expectedState);
        }
    }
}
