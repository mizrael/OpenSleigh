using System;
using System.ComponentModel;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OpenSleigh.Core.Exceptions;
using OpenSleigh.Core.Tests.Sagas;
using OpenSleigh.Core.Utils;
using OpenSleigh.Persistence.Cosmos.SQL.Tests.Fixtures;
using Xunit;

namespace OpenSleigh.Persistence.Cosmos.SQL.Tests.Integration
{

    [Category("Integration")]
    [Trait("Category", "Integration")]
    public class SqlOutboxRepositoryTests : IClassFixture<DbFixture>
    {
        private readonly DbFixture _fixture;

        public SqlOutboxRepositoryTests(DbFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task LockAsync_should_throw_if_message_not_found()
        {
            var message = StartDummySaga.New();
            var sut = CreateSut();
            await Assert.ThrowsAsync<ArgumentException>(async () => await sut.LockAsync(message));
        }

        [Fact]
        public async Task AppendAsync_should_append_message()
        {
            var message = StartDummySaga.New();
            var sut = CreateSut();
            await sut.AppendAsync(message);
            
            var appendedMessage = await _fixture.DbContext.OutboxMessages.FirstOrDefaultAsync(e => e.Id == message.Id);
            appendedMessage.Should().NotBeNull();
            appendedMessage.LockId.Should().BeNull();
            appendedMessage.LockTime.Should().BeNull();
            appendedMessage.Status.Should().Be("Pending");
        }

        [Fact]
        public async Task AppendAsync_should_fail_if_message_already_appended()
        {
            var message = StartDummySaga.New();
            var sut = CreateSut();
            await sut.AppendAsync(message);

            await Assert.ThrowsAsync<InvalidOperationException> (async () => await sut.AppendAsync(message));
        }

        [Fact]
        public async Task ReadMessagesToProcess_should_return_available_messages()
        {
            var message = StartDummySaga.New();
            var sut = CreateSut();
            await sut.AppendAsync(message);

            var messages = await sut.ReadMessagesToProcess();
            messages.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task LockAsync_should_lock_existing_message()
        {
            var message = StartDummySaga.New();
            var sut = CreateSut();
            await sut.AppendAsync(message);

            var lockId = await sut.LockAsync(message);

            var lockedMessage = await _fixture.DbContext.OutboxMessages.FirstOrDefaultAsync(e => e.Id == message.Id);
            lockedMessage.Should().NotBeNull();
            lockedMessage.LockId.Should().Be(lockId);
            lockedMessage.LockTime.Should().NotBeNull();
        }

        [Fact]
        public async Task LockAsync_should_throw_if_message_already_locked()
        {
            var message = StartDummySaga.New();
            var sut = CreateSut();
            await sut.AppendAsync(message);

            await sut.LockAsync(message);

            await Assert.ThrowsAsync<LockException>(async () => await sut.LockAsync(message));
        }

        [Fact]
        public async Task LockAsync_should_throw_if_message_already_processed()
        {
            var message = StartDummySaga.New();
            var sut = CreateSut();

            await sut.AppendAsync(message);
            var lockId = await sut.LockAsync(message);
            await sut.ReleaseAsync(message, lockId);

            await Assert.ThrowsAsync<LockException>(async () => await sut.LockAsync(message));
        }

        [Fact]
        public async Task ReleaseAsync_should_throw_if_message_not_appended()
        {
            var message = StartDummySaga.New();
            var sut = CreateSut();
            await Assert.ThrowsAsync<ArgumentException>(async () => await sut.ReleaseAsync(message, Guid.NewGuid()));
        }

        [Fact]
        public async Task ReleaseAsync_should_throw_if_message_not_locked()
        {
            var message = StartDummySaga.New();
            var sut = CreateSut();

            await sut.AppendAsync(message);

            var ex = await Assert.ThrowsAsync<LockException>(async () => await sut.ReleaseAsync(message, Guid.NewGuid()));
            ex.Message.Should().Contain($"message '{message.Id}' is not locked");
        }

        [Fact]
        public async Task ReleaseAsync_should_throw_if_message_already_locked()
        {
            var message = StartDummySaga.New();
            var sut = CreateSut();

            await sut.AppendAsync(message);
            await sut.LockAsync(message);

            var lockId = Guid.NewGuid();

            var ex = await Assert.ThrowsAsync<LockException>(async () => await sut.ReleaseAsync(message, lockId));
            ex.Message.Should().Contain($"invalid lock id '{lockId}' on message '{message.Id}'");
        }

        [Fact]
        public async Task ReleaseAsync_should_update_message_status()
        {
            var message = StartDummySaga.New();
            var sut = CreateSut();

            await sut.AppendAsync(message);
            var lockId = await sut.LockAsync(message);
            await sut.ReleaseAsync(message, lockId);

            var lockedMessage = await _fixture.DbContext.OutboxMessages.FirstOrDefaultAsync(e => e.Id == message.Id);
            lockedMessage.Status.Should().Be("Processed");
            lockedMessage.LockId.Should().BeNull();
            lockedMessage.LockTime.Should().BeNull();
        }

        [Fact]
        public async Task CleanProcessedAsync_should_clear_processed_messages()
        {
            var sut = CreateSut();

            var messages = new[]
            {
                StartDummySaga.New(),
                StartDummySaga.New(),
                StartDummySaga.New()
            };
            foreach (var message in messages)
                await sut.AppendAsync(message);

            var processedMessage = StartDummySaga.New();

            await sut.AppendAsync(processedMessage);
            var lockId = await sut.LockAsync(processedMessage);
            await sut.ReleaseAsync(processedMessage, lockId);
            
            var count = await _fixture.DbContext.OutboxMessages.CountAsync(e => e.Id == processedMessage.Id);
            count.Should().Be(1);

            await sut.CleanProcessedAsync();

            count = await _fixture.DbContext.OutboxMessages.CountAsync(e => e.Id == processedMessage.Id);
            count.Should().Be(0);
        }

        private CosmosSqlOutboxRepository CreateSut()
        {
            var sut = new CosmosSqlOutboxRepository(_fixture.DbContext, new JsonSerializer(), CosmosSqlOutboxRepositoryOptions.Default);
            return sut;
        }
    }
}
