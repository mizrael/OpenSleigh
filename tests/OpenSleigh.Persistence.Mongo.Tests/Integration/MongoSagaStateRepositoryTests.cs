using MongoDB.Driver;
using OpenSleigh.Persistence.Mongo.Tests.Fixtures;
using OpenSleigh.Transport;
using System.ComponentModel;

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

        private MongoSagaStateRepository CreateSut(IDbContext db, MongoSagaStateRepositoryOptions? options = null)
        {
            var serializer = new JsonSerializer();

            var sut = new MongoSagaStateRepository(db, options, serializer);
            return sut;
        }

        private IMessageContext<TM> CreateMessageContext<TM>() where TM : IMessage
        {
            var messageContext = NSubstitute.Substitute.For<IMessageContext<TM>>();
            messageContext.Id.Returns(Guid.NewGuid().ToString());
            messageContext.CorrelationId.Returns(Guid.NewGuid().ToString());
            return messageContext;
        }

        private ISagaExecutionContext CreateSagaContext()
        {
            var messageContext = CreateMessageContext<FakeMessage>();
            var descriptor = SagaDescriptor.Create<FakeSagaNoState>();

            var factory = new SagaExecutionContextFactory();
            var context = factory.CreateState(descriptor, messageContext);

            return context;
        }

        [Fact]
        public async Task FindAsync_should_return_null_if_not_existing()
        {
            var db = _fixture.CreateDbContext();
            var sut = CreateSut(db);
            var descriptor = SagaDescriptor.Create<FakeSagaNoState>();
            var result = await sut.FindAsync(descriptor, "lorem", CancellationToken.None);
            result.Should().BeNull();
        }

        [Fact]
        public async Task FindAsync_should_return_item_if_existing()
        {
            var db = _fixture.CreateDbContext();
            var sut = CreateSut(db);

            var sagaContext = CreateSagaContext();

            await sut.LockAsync(sagaContext, CancellationToken.None);

            var result = await sut.FindAsync(sagaContext.Descriptor, sagaContext.CorrelationId, CancellationToken.None);
            result.Should().NotBeNull();
            result.InstanceId.Should().Be(sagaContext.InstanceId);
        }

        [Fact]
        public async Task LockAsync_should_lock_item()
        {
            var db = _fixture.CreateDbContext();
            var sut = CreateSut(db);

            var sagaContext = CreateSagaContext();

            var lockId = await sut.LockAsync(sagaContext, CancellationToken.None);

            var filterBuilder = Builders<Entities.SagaState>.Filter;
            var filter = filterBuilder.And(
                filterBuilder.Eq(e => e.LockId, lockId),
                filterBuilder.Eq(e => e.InstanceId, sagaContext.InstanceId),
                filterBuilder.Eq(e => e.CorrelationId, sagaContext.CorrelationId));
            var lockedState = await db.SagaStates.FindOneAsync(filter);
            lockedState.Should().NotBeNull();
        }

        [Fact]
        public async Task LockAsync_should_throw_if_item_already_locked()
        {
            var db = _fixture.CreateDbContext();
            var sut = CreateSut(db);

            var sagaContext = CreateSagaContext();

            var lockId = await sut.LockAsync(sagaContext, CancellationToken.None);

            var ex = await Assert.ThrowsAsync<LockException>(async () => await sut.LockAsync(sagaContext, CancellationToken.None));
            ex.Message.Should().Contain($"saga state '{sagaContext.InstanceId}' is already locked");
        }

        [Fact]
        public async Task LockAsync_should_lock_again_if_first_lock_expired()
        {
            var options = new MongoSagaStateRepositoryOptions(TimeSpan.Zero);
            var db = _fixture.CreateDbContext();
            var sut = CreateSut(db, options);

            var sagaContext = CreateSagaContext();

            var firstLockId = await sut.LockAsync(sagaContext, CancellationToken.None);

            await Task.Delay(500);

            var secondLockId = await sut.LockAsync(sagaContext, CancellationToken.None);

            secondLockId.Should().NotBeNull()
                .And.NotBe(firstLockId);
        }
    }
}
