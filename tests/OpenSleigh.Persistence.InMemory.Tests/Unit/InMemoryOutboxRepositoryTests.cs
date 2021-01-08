using System;
using System.Threading.Tasks;
using FluentAssertions;
using OpenSleigh.Core.Exceptions;
using OpenSleigh.Persistence.InMemory.Messaging;
using Xunit;

namespace OpenSleigh.Persistence.InMemory.Tests.Unit
{
    public class InMemoryOutboxRepositoryTests
    {
        [Fact]
        public async Task ReadMessagesToProcess_should_return_empty_collection_when_no_messages_available()
        {
            var sut = new InMemoryOutboxRepository();
            var results = await sut.ReadMessagesToProcess();
            results.Should().NotBeNull().And.BeEmpty();
        }

        [Fact]
        public async Task CleanProcessedAsync_should_remove_processed_messages()
        {
            var messages = new[]
            {
                DummyMessage.New(),
                DummyMessage.New(),
                DummyMessage.New(),
            };
            var sut = new InMemoryOutboxRepository();

            foreach (var message in messages)
                await sut.AppendAsync(message);

            var results = await sut.ReadMessagesToProcess();
            results.Should().NotBeNull().And.Contain(messages);
            
            foreach (var message in messages)
                await sut.BeginProcessingAsync(message);
            
            await sut.CleanProcessedAsync();
            
            results = await sut.ReadMessagesToProcess();
            results.Should().NotBeNull().And.BeEmpty();
        }

        [Fact]
        public async Task ReadMessagesToProcess_should_return_pending_messages()
        {
            var messages = new[]
            {
                DummyMessage.New(),
                DummyMessage.New(),
                DummyMessage.New(),
            };
            var sut = new InMemoryOutboxRepository();

            foreach (var message in messages)
                await sut.AppendAsync(message);

            var results = await sut.ReadMessagesToProcess();
            results.Should().NotBeNull().And.Contain(messages);
        }

        [Fact]
        public async Task ReadMessagesToProcess_should_not_return_sent_messages()
        {
            var messages = new[]
            {
                DummyMessage.New(),
                DummyMessage.New(),
                DummyMessage.New(),
            };
            var sut = new InMemoryOutboxRepository();

            foreach (var message in messages)
                await sut.AppendAsync(message);

            var lockId = await sut.BeginProcessingAsync(messages[0]);
            await sut.MarkAsSentAsync(messages[0], lockId);

            var results = await sut.ReadMessagesToProcess();
            results.Should().NotBeNull()
                .And.HaveCount(2)
                .And.NotContain(messages[0]);
        }

        [Fact]
        public async Task MarkAsSentAsync_should_throw_when_message_lock_id_invalid()
        {
            var message = DummyMessage.New();
            var sut = new InMemoryOutboxRepository();

            await sut.AppendAsync(message);
            
            await Assert.ThrowsAsync<ArgumentException>(async () => await sut.MarkAsSentAsync(message, Guid.Empty));
        }

        [Fact]
        public async Task MarkAsSentAsync_should_throw_when_message_null()
        {
            var sut = new InMemoryOutboxRepository();
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.MarkAsSentAsync(null, Guid.Empty));
        }

        [Fact]
        public async Task AppendAsync_should_throw_when_message_null()
        {
            var sut = new InMemoryOutboxRepository();
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.AppendAsync(null));
        }

        [Fact]
        public async Task BeginProcessingAsync_should_throw_when_message_already_locked()
        {
            var message = DummyMessage.New(); 
            
            var sut = new InMemoryOutboxRepository();
            await sut.AppendAsync(message);
            await sut.BeginProcessingAsync(message);

            await Assert.ThrowsAsync<LockException>(async () => await sut.BeginProcessingAsync(message));
        }

        [Fact]
        public async Task BeginProcessingAsync_should_throw_when_message_null()
        {
            var sut = new InMemoryOutboxRepository();
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.BeginProcessingAsync(null));
        }
    }
}
