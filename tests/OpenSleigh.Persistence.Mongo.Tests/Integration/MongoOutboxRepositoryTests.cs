using MongoDB.Driver;
using OpenSleigh.Outbox;
using OpenSleigh.Persistence.Mongo.Tests.Fixtures;
using OpenSleigh.Transport;
using System.ComponentModel;

namespace OpenSleigh.Persistence.Mongo.Tests.Integration
{
    [Category("Integration")]
    [Trait("Category", "Integration")]
    public class MongoOutboxRepositoryTests : IClassFixture<DbFixture>
    {
        private readonly DbFixture _fixture;

        public MongoOutboxRepositoryTests(DbFixture fixture)
        {
            _fixture = fixture;
        }

        private static OutboxMessage CreateMessage()
        {
            var sysInfo = NSubstitute.Substitute.For<ISystemInfo>();
            sysInfo.ClientGroup.Returns("test");
            sysInfo.ClientId.Returns("client");
            sysInfo.Id.Returns("sender");
            return OutboxMessage.Create(new FakeMessage(), sysInfo, new JsonSerializer());
        }

        private MongoOutboxRepository CreateSut(IDbContext db)
        {
            var typeResolver = new TypeResolver();
            typeResolver.Register(typeof(FakeMessage));

            var sut = new MongoOutboxRepository(db, MongoOutboxRepositoryOptions.Default, typeResolver);
            return sut;
        }

        [Fact]
        public async Task AppendAsync_should_append_message()
        {
            var message = CreateMessage();

            var db = _fixture.CreateDbContext();
            var sut = CreateSut(db);
            await sut.AppendAsync(new[] { message });

            var filter = Builders<Entities.OutboxMessage>.Filter.Eq(e => e.MessageId, message.MessageId);

            var appendedMessage = await db.OutboxMessages.FindOneAsync(filter);
            appendedMessage.Should().NotBeNull();
            appendedMessage.LockId.Should().BeNull();
            appendedMessage.LockTime.Should().BeNull();
        }

        [Fact]
        public async Task AppendAsync_should_fail_if_message_already_appended()
        {
            var message = CreateMessage();

            var db = _fixture.CreateDbContext();
            var sut = CreateSut(db);
            await sut.AppendAsync(new[] { message });

            await Assert.ThrowsAsync<MongoDB.Driver.MongoBulkWriteException<OpenSleigh.Persistence.Mongo.Entities.OutboxMessage>>(async () => await sut.AppendAsync(new[] { message }));
        }

        [Fact]
        public async Task DeleteAsync_should_throw_if_message_not_found()
        {
            var message = CreateMessage();

            var db = _fixture.CreateDbContext();
            var sut = CreateSut(db);

            var ex = await Assert.ThrowsAsync<ArgumentException>(async () => await sut.DeleteAsync(message, "lorem"));
            ex.Message.Should().Contain($"message '{message.MessageId}' not found");
        }

        [Fact]
        public async Task DeleteAsync_should_throw_if_message_not_locked()
        {
            var message = CreateMessage();
            var db = _fixture.CreateDbContext();
            var sut = CreateSut(db);

            await sut.AppendAsync(new[] { message });

            var ex = await Assert.ThrowsAsync<LockException>(async () => await sut.DeleteAsync(message, "lorem"));
            ex.Message.Should().Contain($"message '{message.MessageId}' is not locked");
        }

        [Fact]
        public async Task DeleteAsync_should_throw_if_lock_invalid()
        {
            var message = CreateMessage();
            var db = _fixture.CreateDbContext();
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

            var db = _fixture.CreateDbContext();
            var sut = CreateSut(db);

            await sut.AppendAsync(new[] { message });
            var lockId = await sut.LockAsync(message);
            await sut.DeleteAsync(message, lockId);

            var filter = Builders<Entities.OutboxMessage>.Filter.Eq(e => e.MessageId, message.MessageId);
            var lockedMessage = await db.OutboxMessages.FindOneAsync(filter);
            lockedMessage.Should().BeNull();
        }

        [Fact]
        public async Task LockAsync_should_throw_if_message_not_found()
        {
            var message = CreateMessage();
            var db = _fixture.CreateDbContext();
            var sut = CreateSut(db);
            await Assert.ThrowsAsync<LockException>(async () => await sut.LockAsync(message));
        }

        [Fact]
        public async Task LockAsync_should_lock_existing_message()
        {
            var message = CreateMessage();
            var db = _fixture.CreateDbContext();
            var sut = CreateSut(db);
            await sut.AppendAsync(new[] { message });

            var lockId = await sut.LockAsync(message);

            var filter = Builders<Entities.OutboxMessage>.Filter.Eq(e => e.MessageId, message.MessageId);
            var lockedMessage = await db.OutboxMessages.FindOneAsync(filter);
            lockedMessage.Should().NotBeNull();
            lockedMessage.LockId.Should().Be(lockId);
            lockedMessage.LockTime.Should().NotBeNull();
        }

        [Fact]
        public async Task LockAsync_should_throw_if_message_already_locked()
        {
            var message = CreateMessage();
            var db = _fixture.CreateDbContext();
            var sut = CreateSut(db);
            await sut.AppendAsync(new[] { message });

            await sut.LockAsync(message);

            await Assert.ThrowsAsync<LockException>(async () => await sut.LockAsync(message));
        }

        [Fact]
        public async Task ReadMessagesToProcess_should_return_available_messages()
        {
            var message = CreateMessage();

            var db = _fixture.CreateDbContext();
            var sut = CreateSut(db);
            await sut.AppendAsync(new[] { message });

            var messages = await sut.ReadPendingAsync();
            messages.Should().NotBeNullOrEmpty();
        }
    }
}
