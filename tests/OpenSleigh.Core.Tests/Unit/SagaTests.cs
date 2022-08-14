using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Persistence;
using OpenSleigh.Core.Tests.Sagas;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit
{
    public class SagaTests
    {
        [Fact]
        public void ctor_should_throw_when_state_null()
        {
            var ex = Assert.Throws<ArgumentNullException>(() => new DummySaga(null));
            ex.ParamName.Should().Be("state");
        }

        [Fact]
        public void SetState_should_set_state()
        {
            var state = new DummySagaState(Guid.NewGuid());
            var sut = new DummySaga(state);
            sut.State.Should().Be(state);
        }

        [Fact]
        public void Publish_should_throw_if_message_null()
        {
            var state = new DummySagaState(Guid.NewGuid());
            var sut = new DummySaga(state);
            Assert.Throws<ArgumentNullException>(() => sut.PublishTestWrapper<DummyMessage>(null));
        }

        [Fact]
        public async Task Publish_should_add_message_to_outbox()
        {
            var message = DummyMessage.New();

            var state = new DummySagaState(Guid.NewGuid());
            var sut = new DummySaga(state);
            sut.PublishTestWrapper(message);

            var outboxRepo = NSubstitute.Substitute.For<IOutboxRepository>();

            await sut.PersistOutboxAsync(outboxRepo, CancellationToken.None);

            await outboxRepo.Received(1)
                      .AppendAsync(
                        Arg.Is<IEnumerable<IMessage>>(msg => msg.Contains(message)),
                        Arg.Any<CancellationToken>());
        }
    }
}
