using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OpenSleigh.Core.Exceptions;
using OpenSleigh.Core.Tests.Sagas;
using OpenSleigh.Core.Utils;
using OpenSleigh.Persistence.SQL.Tests.Fixtures;
using Xunit;

namespace OpenSleigh.Persistence.SQL.Tests.Integration
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

        private SqlOutboxRepository CreateSut()
        {
            var sut = new SqlOutboxRepository(_fixture.DbContext, new JsonSerializer(), SqlOutboxRepositoryOptions.Default);
            return sut;
        }
    }
}
