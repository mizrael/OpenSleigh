using FluentAssertions;
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver;
using OpenSleigh.Core.Exceptions;
using OpenSleigh.Persistence.Mongo.Tests.Fixtures;
using Xunit;
using OpenSleigh.Core.Utils;

namespace OpenSleigh.Persistence.Mongo.Tests.Integration
{
    [Category("Integration")]
    [Trait("Category", "Integration")]
    public class MongoSagaStateRepositoryTests : IClassFixture<DbFixture>
    {
        private readonly DbFixture _fixture;

        public MongoSagaStateRepositoryTests(DbFixture fixture)
        {
            _fixture = fixture;
        }

        private MongoSagaStateRepository CreateSut(IDbContext dbContext, MongoSagaStateRepositoryOptions options = null)
        {
            var serializer = new JsonSerializer();

            options ??= MongoSagaStateRepositoryOptions.Default;

            var sut = new MongoSagaStateRepository(dbContext, serializer, options);
            return sut;
        }

        [Fact]
        public async Task LockAsync_should_create_and_return_locked_item_if_not_existing()
        {
            var (db,_) = _fixture.CreateDbContext();
            var sut = CreateSut(db);

            var newState = DummyState.New();

            var (state, lockId) = await sut.LockAsync(newState.Id, newState, CancellationToken.None);
            state.Should().NotBeNull();
            state.Id.Should().Be(newState.Id);
            state.Bar.Should().Be(newState.Bar);
            state.Foo.Should().Be(newState.Foo);
        }

        [Fact]
        public async Task LockAsync_should_lock_item()
        {
            var (db,_) = _fixture.CreateDbContext();
            var sut = CreateSut(db);

            var newState = DummyState.New();

            var (state, lockId) = await sut.LockAsync(newState.Id, newState, CancellationToken.None);

            var filter = Builders<Entities.SagaState>.Filter.Eq(e => e.LockId, lockId);
            var cursor = await db.SagaStates.FindAsync(filter);
            var lockedState = await cursor.FirstOrDefaultAsync();
            lockedState.Should().NotBeNull();
        }

        [Fact]
        public async Task LockAsync_should_throw_if_item_locked()
        {
            var (db,_) = _fixture.CreateDbContext();
            var sut = CreateSut(db);

            var newState = DummyState.New();

            await sut.LockAsync(newState.Id, newState, CancellationToken.None);

            var ex = await Assert.ThrowsAsync<LockException>(async () => await sut.LockAsync(newState.Id, newState, CancellationToken.None));
            ex.Message.Should().Contain($"saga state '{newState.Id}' is already locked");
        }

        [Fact]
        public async Task LockAsync_should_return_state_if_lock_expired()
        {
            var (db,_) = _fixture.CreateDbContext();
            var options = new MongoSagaStateRepositoryOptions(TimeSpan.Zero);
            var sut = CreateSut(db, options);

            var newState = DummyState.New();

            var (firstState, firstLockId) = await sut.LockAsync(newState.Id, newState, CancellationToken.None);

            await Task.Delay(500);
            
            var (secondState, secondLockId) = await sut.LockAsync(newState.Id, newState, CancellationToken.None);
            secondLockId.Should().NotBe(firstLockId);
            secondState.Should().NotBeNull();
        }

        [Fact]
        public async Task LockAsync_should_allow_different_saga_state_types_to_share_the_correlation_id()
        {
            var (db,_) = _fixture.CreateDbContext();
            var sut = CreateSut(db);

            var correlationId = Guid.NewGuid();

            var newState = new DummyState(correlationId, "lorem", 42);
            
            var (state, lockId) = await sut.LockAsync(correlationId, newState, CancellationToken.None);

            var newState2 = new DummyState2(state.Id);
            newState2.Id.Should().Be(newState.Id);
            
            var (state2, lockId2) = await sut.LockAsync(correlationId, newState2, CancellationToken.None);
            state2.Should().NotBeNull();
            state2.Id.Should().Be(correlationId);
        }

    }
}
