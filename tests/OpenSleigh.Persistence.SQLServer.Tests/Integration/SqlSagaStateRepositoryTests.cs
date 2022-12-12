using OpenSleigh.Messaging;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Persistence.SQL.Tests;
using OpenSleigh.Persistence.SQLServer.Tests.Fixtures;
using OpenSleigh.Utils;
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Persistence.SQLServer.Tests.Integration
{
    [Category("Integration")]
    [Trait("Category", "Integration")]
    public class SqlSagaStateRepositoryTests : IClassFixture<DbFixture>
    {
        private readonly DbFixture _fixture;

        public SqlSagaStateRepositoryTests(DbFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task FindAsync_should_return_null_if_not_existing()
        {
            var (db,_) = _fixture.CreateDbContext();
            var sut = CreateSut(db);
            var descriptor = SagaDescriptor.Create<FakeSagaNoState>();
            var result = await sut.FindAsync(descriptor, "lorem", CancellationToken.None);
            result.Should().BeNull();
        }

        [Fact]
        public async Task LockAsync_should_lock_item()
        {
            var (db, _) = _fixture.CreateDbContext();
            var sut = CreateSut(db);

            var sagaContext = CreateSagaContext();

            var lockId = await sut.LockAsync(sagaContext, CancellationToken.None);

            var lockedState = await db.SagaStates.FirstOrDefaultAsync(e =>
                                        e.LockId == lockId &&
                                        e.InstanceId == sagaContext.InstanceId &&
                                        e.CorrelationId == sagaContext.CorrelationId);
            lockedState.Should().NotBeNull();
        }

        //[Fact]
        //public async Task LockAsync_should_throw_if_item_locked()
        //{
        //    var (db,_) = _fixture.CreateDbContext();
        //    var sut = CreateSut(db);

        //    var newState = DummyState.New();

        //    await sut.LockAsync(newState.Id, newState, CancellationToken.None);

        //    var ex = await Assert.ThrowsAsync<LockException>(async () => await sut.LockAsync(newState.Id, newState, CancellationToken.None));
        //    ex.Message.Should().Contain($"saga state '{newState.Id}' is already locked");
        //}

        //[Fact]
        //public async Task LockAsync_should_return_state_if_lock_expired()
        //{
        //    var options = new SqlSagaStateRepositoryOptions(TimeSpan.Zero);
        //    var (db,_) = _fixture.CreateDbContext();
        //    var sut = CreateSut(db, options);

        //    var newState = DummyState.New();

        //    var (firstState, firstLockId) = await sut.LockAsync(newState.Id, newState, CancellationToken.None);

        //    await Task.Delay(500);

        //    var (secondState, secondLockId) = await sut.LockAsync(newState.Id, newState, CancellationToken.None);
        //    secondLockId.Should().NotBe(firstLockId);
        //    secondState.Should().NotBeNull();
        //}

        //[Fact]
        //public async Task LockAsync_should_allow_different_saga_state_types_to_share_the_correlation_id()
        //{
        //    var (db,_) = _fixture.CreateDbContext();
        //    var sut = CreateSut(db);

        //    var correlationId = Guid.NewGuid();

        //    var newState = new DummyState(correlationId, "lorem", 42);

        //    var (state, lockId) = await sut.LockAsync(correlationId, newState, CancellationToken.None);

        //    var newState2 = new DummyState2(state.Id);
        //    newState2.Id.Should().Be(newState.Id);

        //    var (state2, lockId2) = await sut.LockAsync(correlationId, newState2, CancellationToken.None);
        //    state2.Should().NotBeNull();
        //    state2.Id.Should().Be(correlationId);
        //}

        //[Fact]
        //public async Task ReleaseLockAsync_should_throw_when_state_not_found()
        //{
        //    var (db,_) = _fixture.CreateDbContext();
        //    var sut = CreateSut(db);

        //    var correlationId = Guid.NewGuid();
        //    var state = new DummyState(correlationId, "lorem", 42);

        //    var ex = await Assert.ThrowsAsync<LockException>(async () => await sut.ReleaseLockAsync(state, Guid.NewGuid()));
        //    ex.Message.Should().Contain($"unable to release Saga State");
        //}

        //[Fact]
        //public async Task ReleaseLockAsync_should_release_lock_and_update_state()
        //{
        //    var (db,_) = _fixture.CreateDbContext();
        //    var sut = CreateSut(db);

        //    var correlationId = Guid.NewGuid();
        //    var newState = new DummyState(correlationId, "lorem", 42);

        //    var (state, lockId) = await sut.LockAsync(correlationId, newState, CancellationToken.None);

        //    var updatedState = new DummyState(correlationId, "ipsum", 71);
        //    await sut.ReleaseLockAsync(updatedState, lockId);

        //    var unLockedState = await db.SagaStates.FirstOrDefaultAsync(e => e.CorrelationId == newState.Id);
        //    unLockedState.Should().NotBeNull();
        //    unLockedState.LockId.Should().BeNull();
        //    unLockedState.LockTime.Should().BeNull();
        //    unLockedState.Data.Should().NotBeNull();

        //    var serializer = new JsonSerializer();
        //    var deserializedState = serializer.Deserialize<DummyState>(unLockedState.Data);
        //    deserializedState.Id.Should().Be(updatedState.Id);
        //    deserializedState.Bar.Should().Be(updatedState.Bar);
        //    deserializedState.Foo.Should().Be(updatedState.Foo);
        //}

        private SqlSagaStateRepository CreateSut(ISagaDbContext db,
            SqlSagaStateRepositoryOptions options = null)
        {
            var serializer = new JsonSerializer();
            var sut = new SqlSagaStateRepository(db, options ?? SqlSagaStateRepositoryOptions.Default, serializer);
            return sut;
        }

        private ISagaExecutionContext CreateSagaContext()
        {
            var messageContext = NSubstitute.Substitute.For<IMessageContext<FakeMessage>>();
            messageContext.Id.Returns(Guid.NewGuid().ToString());
            messageContext.CorrelationId.Returns(Guid.NewGuid().ToString());

            var descriptor = SagaDescriptor.Create<FakeSagaNoState>();

            var factory = new SagaExecutionContextFactory();
            var context = factory.CreateState(descriptor, messageContext);

            return context;
        }
    }
}
