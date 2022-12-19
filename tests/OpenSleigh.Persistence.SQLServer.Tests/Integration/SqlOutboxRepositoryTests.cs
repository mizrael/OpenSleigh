using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OpenSleigh.Transport;
using OpenSleigh.Outbox;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Persistence.SQL.Tests;
using OpenSleigh.Persistence.SQLServer.Tests.Fixtures;
using OpenSleigh.Utils;
using System;
using System.ComponentModel;
using System.Diagnostics.Eventing.Reader;
using System.Threading.Tasks;
using Xunit;

namespace OpenSleigh.Persistence.SQLServer.Tests.Integration
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

        private static OutboxMessage CreateMessage()
            => OutboxMessage.Create(new FakeMessage(), new JsonSerializer());

        [Fact]
        public async Task LockAsync_should_throw_if_message_not_found()
        {
            var message = CreateMessage();

            var (db,_) = _fixture.CreateDbContext();
            var sut = CreateSut(db);
            await Assert.ThrowsAsync<ArgumentException>(async () => await sut.LockAsync(message));
        }

        [Fact]
        public async Task AppendAsync_should_append_message()
        {
            var message = CreateMessage();

            var (db,_) = _fixture.CreateDbContext();
            var sut = CreateSut(db);
            await sut.AppendAsync(new[] { message });

            var appendedMessage = await db.OutboxMessages.FirstOrDefaultAsync(e => e.MessageId == message.MessageId);
            appendedMessage.Should().NotBeNull();
            appendedMessage.LockId.Should().BeNull();
            appendedMessage.LockTime.Should().BeNull();
        }

        [Fact]
        public async Task AppendAsync_should_fail_if_message_already_appended()
        {
            var message = CreateMessage();

            var (db,_) = _fixture.CreateDbContext();
            var sut = CreateSut(db);
            await sut.AppendAsync(new[] { message });

            await Assert.ThrowsAsync<InvalidOperationException> (async () => await sut.AppendAsync(new[] { message }));
        }

        [Fact]
        public async Task ReadMessagesToProcess_should_return_available_messages()
        {
            var message = CreateMessage();

            var (db,_) = _fixture.CreateDbContext();
            var sut = CreateSut(db);
            await sut.AppendAsync(new[] { message });

            var messages = await sut.ReadPendingAsync();
            messages.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task LockAsync_should_lock_existing_message()
        {
            var message = CreateMessage();

            var (db,_) = _fixture.CreateDbContext();
            var sut = CreateSut(db);
            await sut.AppendAsync(new[] { message });

            var lockId = await sut.LockAsync(message);

            var lockedMessage = await db.OutboxMessages.FirstOrDefaultAsync(e => e.MessageId == message.MessageId);
            lockedMessage.Should().NotBeNull();
            lockedMessage.LockId.Should().Be(lockId);
            lockedMessage.LockTime.Should().NotBeNull();
        }

        [Fact]
        public async Task LockAsync_should_throw_if_message_already_locked()
        {
            var message = CreateMessage();

            var (db,_) = _fixture.CreateDbContext();
            var sut = CreateSut(db);
            await sut.AppendAsync(new[] { message });

            await sut.LockAsync(message);

            await Assert.ThrowsAsync<LockException>(async () => await sut.LockAsync(message));
        }

        [Fact]
        public async Task LockAsync_should_throw_if_message_not_existing()
        {
            var message = CreateMessage();

            var (db,_) = _fixture.CreateDbContext();
            var sut = CreateSut(db);

            await Assert.ThrowsAsync<ArgumentException>(async () => await sut.LockAsync(message));
        }

        [Fact]
        public async Task DeleteAsync_should_throw_if_message_not_found()
        {
            var message = CreateMessage();

            var (db,_) = _fixture.CreateDbContext();
            var sut = CreateSut(db);

            var ex = await Assert.ThrowsAsync<ArgumentException>(async () => await sut.DeleteAsync(message, "lorem"));
            ex.Message.Should().Contain($"message '{message.MessageId}' not found");
        }

        [Fact]
        public async Task DeleteAsync_should_throw_if_message_not_locked()
        {
            var message = CreateMessage();
            var (db,_) = _fixture.CreateDbContext();
            var sut = CreateSut(db);

            await sut.AppendAsync(new[] { message });

            var ex = await Assert.ThrowsAsync<LockException>(async () => await sut.DeleteAsync(message, "lorem"));
            ex.Message.Should().Contain($"message '{message.MessageId}' is not locked");
        }

        [Fact]
        public async Task DeleteAsync_should_throw_if_lock_invalid()
        {
            var message = CreateMessage();
            var (db,_) = _fixture.CreateDbContext();
            var sut = CreateSut(db);

            await sut.AppendAsync(new[] { message });
            await sut.LockAsync(message);

            var lockId = Guid.NewGuid().ToString();

            var ex = await Assert.ThrowsAsync<LockException>(async () => await sut.DeleteAsync(message, lockId));
            ex.Message.Should().Contain($"invalid lock id '{lockId}' on message '{message.MessageId}'");
        }

        [Fact]
        public async Task DeleteAsync_should_delete_message()
        {
            var message = CreateMessage();

            var (db,_) = _fixture.CreateDbContext();
            var sut = CreateSut(db);

            await sut.AppendAsync(new[] { message });
            var lockId = await sut.LockAsync(message);
            await sut.DeleteAsync(message, lockId);

            var lockedMessage = await db.OutboxMessages.FirstOrDefaultAsync(e => e.MessageId == message.MessageId);
            lockedMessage.Should().BeNull();
        }

        private SqlOutboxRepository CreateSut(ISagaDbContext db)
        {
            var typeResolver = new TypeResolver();
            typeResolver.Register(typeof(FakeMessage));

            var sut = new SqlOutboxRepository(db, typeResolver, SqlOutboxRepositoryOptions.Default);
            return sut;
        }
    }
}
