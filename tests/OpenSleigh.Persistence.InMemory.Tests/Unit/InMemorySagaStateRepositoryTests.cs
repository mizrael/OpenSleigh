using FluentAssertions;
using OpenSleigh.Core.Exceptions;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OpenSleigh.Persistence.InMemory.Tests.Unit
{
    public class InMemorySagaStateRepositoryTests
    {
        [Fact]
        public async Task ReleaseLockAsync_should_throw_if_input_null()
        {
            var sut = new InMemorySagaStateRepository();

            await Assert.ThrowsAsync<ArgumentNullException>(async () => await sut.ReleaseLockAsync<DummyState>(null, Guid.Empty));
        }
        
        [Fact]
        public async Task LockAsync_should_create_and_return_locked_item_if_not_existing()
        {
            var sut = new InMemorySagaStateRepository();

            var newState = DummyState.New();
            var (state, lockId) = await sut.LockAsync(newState.Id, newState, CancellationToken.None);
            state.Should().NotBeNull();
            state.Id.Should().Be(newState.Id);
            state.Bar.Should().Be(newState.Bar);
            state.Foo.Should().Be(newState.Foo);
        }

        [Fact]
        public async Task LockAsync_should_fail_if_item_already_locked()
        {
            var sut = new InMemorySagaStateRepository();

            var newState = DummyState.New();

            var (lockedState, lockId) = await sut.LockAsync(newState.Id, newState, CancellationToken.None);
            lockedState.Should().NotBeNull();

            await Assert.ThrowsAsync<LockException>(async () => await sut.LockAsync(newState.Id, newState, CancellationToken.None));
        }

        [Fact]
        public async Task LockAsync_should_lock_item_if_available()
        {
            var sut = new InMemorySagaStateRepository();

            var newState = DummyState.New();

            var (lockedState, lockId) = await sut.LockAsync(newState.Id, newState);
            await sut.ReleaseLockAsync(lockedState, lockId);

            var (secondLockedState, secondLockId) = await sut.LockAsync<DummyState>(newState.Id);
            secondLockedState.Should().NotBeNull();
            secondLockId.Should().NotBe(lockId);
        }

        [Fact]
        public async Task LockAsync_should_allow_different_saga_state_types_to_share_the_correlation_id()
        {
            var sut = new InMemorySagaStateRepository();

            var correlationId = Guid.NewGuid();

            var newState = new DummyState(correlationId, "lorem", 42);
            var (state, lockId) = await sut.LockAsync(correlationId, newState, CancellationToken.None);

            var newState2 = new DummyState2(correlationId);
            newState2.Id.Should().Be(newState.Id);

            var (state2, lockId2) = await sut.LockAsync(correlationId, newState2, CancellationToken.None);
            state2.Should().NotBeNull();
            state2.Id.Should().Be(correlationId);
        }

        [Fact]
        public async Task UpdateAsync_should_release_lock()
        {
            var sut = new InMemorySagaStateRepository();

            var newState = DummyState.New();
            var (lockedState, lockId) = await sut.LockAsync(newState.Id, newState, CancellationToken.None);

            var updatedItem = new DummyState(lockedState.Id, "dolor amet", 71);

            await sut.ReleaseLockAsync(updatedItem, lockId, null, CancellationToken.None);
        }

        [Fact]
        public async Task UpdateAsync_should_fail_if_item_not_locked()
        {
            var sut = new InMemorySagaStateRepository();

            var newState = DummyState.New();

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await sut.ReleaseLockAsync(newState, Guid.NewGuid(), null, CancellationToken.None));
        }

        [Fact]
        public async Task UpdateAsync_should_fail_if_item_not_locked_anymore()
        {
            var sut = new InMemorySagaStateRepository();

            var newState = DummyState.New();
            var (lockedState, lockId) = await sut.LockAsync(newState.Id, newState, CancellationToken.None);

            var updatedItem = new DummyState(lockedState.Id, "dolor amet", 71);

            await sut.ReleaseLockAsync(updatedItem, lockId, null, CancellationToken.None);

            await Assert.ThrowsAsync<LockException>(async () => await sut.ReleaseLockAsync(updatedItem, lockId, null, CancellationToken.None));
        }

        [Fact]
        public async Task UpdateAsync_should_fail_if_item_locked_by_somebody_else()
        {
            var sut = new InMemorySagaStateRepository();

            var newState = DummyState.New();
            var (lockedState, lockId) = await sut.LockAsync(newState.Id, newState, CancellationToken.None);

            var updatedItem = new DummyState(lockedState.Id, "dolor amet", 71);

            await Assert.ThrowsAsync<LockException>(async () => await sut.ReleaseLockAsync(updatedItem, Guid.NewGuid(), null, CancellationToken.None));
        }
    }
}
