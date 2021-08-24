using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OpenSleigh.Core.Exceptions;
using OpenSleigh.Core.Utils;
using OpenSleigh.Persistence.Cosmos.SQL.Tests.Fixtures;
using Xunit;

namespace OpenSleigh.Persistence.Cosmos.SQL.Tests.Integration
{
    [Category("Integration")]
    [Trait("Category", "Integration")]
    public class CosmosSqlSagaStateRepositoryTests : IClassFixture<DbFixture>
    {
        private readonly DbFixture _fixture;

        public CosmosSqlSagaStateRepositoryTests(DbFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task LockAsync_should_create_and_return_locked_item_if_not_existing()
        {
            var (dbContext, _) = _fixture.CreateDbContext();
            var sut = CreateSut(dbContext);

            var newState = DummyState.New();

            var (state, _) = await sut.LockAsync(newState.Id, newState, CancellationToken.None);
            state.Should().NotBeNull();
            state.Id.Should().Be(newState.Id);
            state.Bar.Should().Be(newState.Bar);
            state.Foo.Should().Be(newState.Foo);
        }

        [Fact]
        public async Task LockAsync_should_lock_item()
        {
            var (dbContext, _) = _fixture.CreateDbContext();
            var sut = CreateSut(dbContext);

            var newState = DummyState.New();

            var (_, lockId) = await sut.LockAsync(newState.Id, newState, CancellationToken.None);
                        
            var lockedState = await dbContext.SagaStates.FirstOrDefaultAsync(e =>
                                        e.LockId == lockId && e.CorrelationId == newState.Id);
            lockedState.Should().NotBeNull();
        }

        [Fact]
        public async Task LockAsync_should_throw_if_item_locked()
        {
            var (dbContext, _) = _fixture.CreateDbContext();
            var sut = CreateSut(dbContext);

            var newState = DummyState.New();

            await sut.LockAsync(newState.Id, newState, CancellationToken.None);

            var ex = await Assert.ThrowsAsync<LockException>(async () => await sut.LockAsync(newState.Id, newState, CancellationToken.None));
            ex.Message.Should().Contain($"saga state '{newState.Id}' is already locked");
        }

        [Fact]
        public async Task LockAsync_should_return_state_if_lock_expired()
        {
            var options = new CosmosSqlSagaStateRepositoryOptions(TimeSpan.Zero);
            var (dbContext, _) = _fixture.CreateDbContext();
            var sut = CreateSut(dbContext, options);

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
            var (dbContext, _) = _fixture.CreateDbContext();
            var sut = CreateSut(dbContext);

            var correlationId = Guid.NewGuid();

            var newState = new DummyState(correlationId, "lorem", 42);

            var (state, lockId) = await sut.LockAsync(correlationId, newState, CancellationToken.None);

            var newState2 = new DummyState2(state.Id);
            newState2.Id.Should().Be(newState.Id);

            var (state2, lockId2) = await sut.LockAsync(correlationId, newState2, CancellationToken.None);
            state2.Should().NotBeNull();
            state2.Id.Should().Be(correlationId);
        }

        [Fact]
        public async Task ReleaseLockAsync_should_throw_when_state_not_found()
        {
            var (dbContext, _) = _fixture.CreateDbContext();
            var sut = CreateSut(dbContext);

            var correlationId = Guid.NewGuid();
            var state = new DummyState(correlationId, "lorem", 42);

            var ex = await Assert.ThrowsAsync<LockException>(async () => await sut.ReleaseLockAsync(state, Guid.NewGuid()));
            ex.Message.Should().Contain($"unable to release Saga State '{state.Id}'");
        }

        [Fact]
        public async Task ReleaseLockAsync_should_release_lock_and_update_state()
        {
            var (dbContext, _) = _fixture.CreateDbContext();
            var sut = CreateSut(dbContext);

            var correlationId = Guid.NewGuid();
            var newState = new DummyState(correlationId, "lorem", 42);

            var (state, lockId) = await sut.LockAsync(correlationId, newState, CancellationToken.None);
            
            var updatedState = new DummyState(correlationId, "ipsum", 71);
            await sut.ReleaseLockAsync(updatedState, lockId);

            var unLockedState = await dbContext.SagaStates.FirstOrDefaultAsync(e => e.CorrelationId == newState.Id);
            unLockedState.Should().NotBeNull();
            unLockedState.LockId.Should().BeNull();
            unLockedState.LockTime.Should().BeNull();
            unLockedState.Data.Should().NotBeNull();

            var serializer = new JsonSerializer();
            var deserializedState = await serializer.DeserializeAsync<DummyState>(unLockedState.Data.Span);
            deserializedState.Id.Should().Be(updatedState.Id);
            deserializedState.Bar.Should().Be(updatedState.Bar);
            deserializedState.Foo.Should().Be(updatedState.Foo);
        }

        private CosmosSqlSagaStateRepository CreateSut(ISagaDbContext dbContext,
            CosmosSqlSagaStateRepositoryOptions options = null)
        {
            var serializer = new JsonSerializer();
            
            var sut = new CosmosSqlSagaStateRepository(dbContext, serializer, options ?? CosmosSqlSagaStateRepositoryOptions.Default);
            return sut;
        }
    }
}
