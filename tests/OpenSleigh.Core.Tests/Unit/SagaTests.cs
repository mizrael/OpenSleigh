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

            // the outbox collection is mutated inside PersistOutboxAsync()
            // which would make calls to .Received() fail.
            var called = false;
            var outboxRepo = NSubstitute.Substitute.For<IOutboxRepository>();
            outboxRepo.WhenForAnyArgs(repo =>
            {
                repo.AppendAsync(null, default);
            }).Do(ci =>
            {
                var messages = ci.ArgAt<IEnumerable<IMessage>>(0);
                called = messages != null && messages.Any(msg => msg.Id == message.Id);
            });

            await sut.PersistOutboxAsync(outboxRepo, CancellationToken.None);

            called.Should().BeTrue();
        }

        [Fact]
        public async Task PersistOutboxAsync_should_empty_outbox()
        {
            var message = DummyMessage.New();

            var state = new DummySagaState(Guid.NewGuid());
            var sut = new DummySaga(state);
            sut.PublishTestWrapper(message);

            // the outbox collection is mutated inside PersistOutboxAsync()
            // which would make calls to .Received() fail.
            var called = false;
            var outboxRepo = NSubstitute.Substitute.For<IOutboxRepository>();
            outboxRepo.WhenForAnyArgs(repo =>
            {
                repo.AppendAsync(null, default);
            }).Do(ci =>
            {
                var messages = ci.ArgAt<IEnumerable<IMessage>>(0);
                called = messages != null && messages.Any(msg => msg.Id == message.Id);
            });

            await sut.PersistOutboxAsync(outboxRepo, CancellationToken.None);
            called.Should().BeTrue();

            await sut.PersistOutboxAsync(outboxRepo, CancellationToken.None);
            called.Should().BeFalse();
        }
    }
}
