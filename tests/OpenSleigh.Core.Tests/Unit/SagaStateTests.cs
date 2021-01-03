using FluentAssertions;
using NSubstitute;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit
{
    public class SagaStateTests
    {
        [Fact]
        public void SetAsProcessed_should_throw_ArgumentNullException_if_message_null()
        {
            var sut = new DummySagaState(Guid.NewGuid());

            Assert.Throws<ArgumentNullException>(() => sut.SetAsProcessed((IMessage) null));
        }

        [Fact]
        public void SetAsProcessed_should_throw_ArgumentException_if_message_correlation_id_invalid()
        {
            var sut = new DummySagaState(Guid.NewGuid());
            var message = StartDummySaga.New();
            
            Assert.Throws<ArgumentException>(() => sut.SetAsProcessed(message));
        }


        [Fact]
        public void CheckWasProcessed_should_return_false_if_message_not_processed()
        {
            var sut = new DummySagaState(Guid.NewGuid());

            var message = new StartDummySaga(Guid.NewGuid(), sut.Id);
            sut.CheckWasProcessed(message).Should().BeFalse();
        }

        [Fact]
        public void CheckWasProcessed_should_return_true_if_message_processed()
        {
            var sut = new DummySagaState(Guid.NewGuid());
            var message = new StartDummySaga(Guid.NewGuid(), sut.Id);
            sut.SetAsProcessed(message);
            sut.CheckWasProcessed(message).Should().BeTrue();
        }

        [Fact]
        public void CheckWasProcessed_should_throw_ArgumentNullException_if_message_null()
        {
            var sut = new DummySagaState(Guid.NewGuid());
            
            Assert.Throws<ArgumentNullException>(() => sut.CheckWasProcessed((IMessage)null));
        }
    }
}
