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
        public void Enqueue_should_enqueue_message()
        {
            var state = new DummySagaState(Guid.NewGuid());
            state.Outbox.Should().BeEmpty();

            var msg = StartDummySaga.New();
            state.AddToOutbox(msg);
            state.Outbox.Should().HaveCount(1)
                .And.Contain(m => m.Id == msg.Id);
        }

        [Fact]
        public void AddToOutbox_should_not_enqueue_the_same_message_more_than_once()
        {
            var state = new DummySagaState(Guid.NewGuid());
            state.Outbox.Should().BeEmpty();

            var msg = StartDummySaga.New();
            state.AddToOutbox(msg);

            var ex = Assert.Throws<ArgumentException>(() => state.AddToOutbox(msg));
            ex.Message.Should().Contain($"message '{msg.Id}' was already enqueued");
        }

        [Fact]
        public async Task AddToOutbox_should_not_enqueue_a_message_already_sent()
        {
            var state = new DummySagaState(Guid.NewGuid());
            state.Outbox.Should().BeEmpty();

            var msg = StartDummySaga.New();
            state.AddToOutbox(msg);
            await state.ProcessOutboxAsync(NSubstitute.Substitute.For<IMessageBus>());

            var ex = Assert.Throws<ArgumentException>(() => state.AddToOutbox(msg));
            ex.Message.Should().Contain($"message '{msg.Id}' was already sent");
        }

        [Fact]
        public async Task ProcessOutbox_should_publish_all_messaged()
        {
            var state = new DummySagaState(Guid.NewGuid());

            var messages = Enumerable.Repeat(1, 5)
                .Select(i => StartDummySaga.New())
                .ToArray();
            foreach (var msg in messages)
                state.AddToOutbox(msg);

            var bus = NSubstitute.Substitute.For<IMessageBus>();
            await state.ProcessOutboxAsync(bus, CancellationToken.None);

            foreach (var msg in messages)
                await bus.Received()
                    .PublishAsync(msg, CancellationToken.None);

            state.Outbox.Should().BeEmpty();

            foreach (var msg in messages)
                state.CheckWasPublished(msg).Should().BeTrue();
        }

        [Fact]
        public async Task ProcessOutbox_should_reenqueue_failed_messages()
        {
            var state = new DummySagaState(Guid.NewGuid());

            var messages = Enumerable.Repeat(1, 3)
                .Select(i => StartDummySaga.New())
                .ToArray();
            foreach (var msg in messages)
                state.AddToOutbox(msg);

            var bus = NSubstitute.Substitute.For<IMessageBus>();

            var failedMessage = Enumerable.Repeat(1, 5)
                .Select(i => StartDummySaga.New())
                .ToArray();
            foreach (var msg in failedMessage)
            {
                state.AddToOutbox(msg);
                bus.When(b => b.PublishAsync(msg, CancellationToken.None))
                    .Throw(new Exception(msg.Id.ToString()));
            }

            state.Outbox.Should().HaveCount(failedMessage.Length + messages.Length);

            await state.ProcessOutboxAsync(bus, CancellationToken.None);

            foreach (var msg in messages)
                await bus.Received()
                    .PublishAsync(msg, CancellationToken.None);

            state.Outbox.Should().HaveCount(failedMessage.Length);

            foreach (var msg in failedMessage)
                state.CheckWasPublished(msg).Should().BeFalse();
        }
    }
}
