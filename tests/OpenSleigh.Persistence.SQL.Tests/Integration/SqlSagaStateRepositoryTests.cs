using OpenSleigh.Persistence.SQL.Tests.Fixtures;
using OpenSleigh.Transport;
using OpenSleigh.Utils;
using System.ComponentModel;

namespace OpenSleigh.Persistence.SQL.Tests.Integration;

[Category("Integration")]
[Trait("Category", "Integration")]
public abstract class SqlSagaStateRepositoryTests 
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
        var messageContext = CreateMessageContext<FakeMessage>();

        var result = await sut.FindAsync(descriptor, messageContext, CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task FindAsync_should_return_item_if_existing()
    {
        var (db, _) = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        var messageContext = CreateMessageContext<FakeMessage>();
        var sagaContext = CreateSagaContext(messageContext);

        await sut.LockAsync(sagaContext, CancellationToken.None);

        var result = await sut.FindAsync(sagaContext.Descriptor, messageContext, CancellationToken.None);
        result.Should().NotBeNull();
        result.InstanceId.Should().Be(sagaContext.InstanceId);
    }

    [Fact]
    public async Task LockAsync_should_lock_item()
    {
        var (db, _) = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        var messageContext = CreateMessageContext<FakeMessage>();
        var sagaContext = CreateSagaContext(messageContext);

        var lockId = await sut.LockAsync(sagaContext, CancellationToken.None);

        var lockedState = await db.SagaStates.FirstOrDefaultAsync(e =>
                                    e.LockId == lockId &&
                                    e.InstanceId == sagaContext.InstanceId &&
                                    e.CorrelationId == sagaContext.CorrelationId);
        lockedState.Should().NotBeNull();
    }

    [Fact]
    public async Task LockAsync_should_throw_if_item_already_locked()
    {
        var (db, _) = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        var messageContext = CreateMessageContext<FakeMessage>();
        var sagaContext = CreateSagaContext(messageContext);

        var lockId = await sut.LockAsync(sagaContext, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<LockException>(async () => await sut.LockAsync(sagaContext, CancellationToken.None));
        ex.Message.Should().Contain($"saga state '{sagaContext.InstanceId}' is already locked");
    }

    [Fact]
    public async Task LockAsync_should_lock_again_if_first_lock_expired()
    {
        var options = new SqlSagaStateRepositoryOptions(TimeSpan.Zero);
        var (db, _) = _fixture.CreateDbContext();
        var sut = CreateSut(db, options);

        var messageContext = CreateMessageContext<FakeMessage>();
        var sagaContext = CreateSagaContext(messageContext);
            
        var firstLockId = await sut.LockAsync(sagaContext, CancellationToken.None);

        await Task.Delay(500);

        var messageContext2 = CreateMessageContext<FakeMessage>();
        var secondLockId = await sut.LockAsync(sagaContext, CancellationToken.None);

        secondLockId.Should().NotBeNull()
            .And.NotBe(firstLockId);
    }

    [Fact]
    public async Task ReleaseLockAsync_should_throw_when_state_not_found()
    {
        var options = new SqlSagaStateRepositoryOptions(TimeSpan.Zero);
        var (db, _) = _fixture.CreateDbContext();
        var sut = CreateSut(db, options);

        var messageContext = CreateMessageContext<FakeMessage>();
        var sagaContext = CreateSagaContext(messageContext);

        var ex = await Assert.ThrowsAsync<ArgumentException>(async () => await sut.ReleaseAsync(sagaContext));
        ex.Message.Should().Contain($"saga state '{sagaContext.InstanceId}' not found");
    }

    [Fact]
    public async Task ReleaseLockAsync_should_throw_when_lock_invalid()
    {
        var options = new SqlSagaStateRepositoryOptions(TimeSpan.Zero);
        var (db, _) = _fixture.CreateDbContext();
        var sut = CreateSut(db, options);

        var messageContext = CreateMessageContext<FakeMessage>();
        var sagaContext = CreateSagaContext(messageContext);

        await sut.LockAsync(sagaContext, CancellationToken.None);

        var fakeContext = NSubstitute.Substitute.For<ISagaExecutionContext>();
        fakeContext.InstanceId.Returns(sagaContext.InstanceId);
        fakeContext.LockId.Returns("lorem");

        var ex = await Assert.ThrowsAsync<LockException>(async () => await sut.ReleaseAsync(fakeContext));
        ex.Message.Should().Contain($"unable to release Saga State '{sagaContext.InstanceId}' with lock id 'lorem'");
    }

    [Fact]
    public async Task ReleaseLockAsync_should_release_lock_and_update_state()
    {
        var (db, _) = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        var messageContext = CreateMessageContext<FakeMessage>();
        var sagaContext = CreateSagaContext(messageContext);

        var messageContext2 = CreateMessageContext<FakeMessage>();

        await sagaContext.LockAsync(sut, CancellationToken.None);

        sagaContext.SetAsProcessed(messageContext);
        sagaContext.SetAsProcessed(messageContext2);

        sagaContext.MarkAsCompleted();

        await sut.ReleaseAsync(sagaContext);

        var unLockedState = await db.SagaStates.FirstOrDefaultAsync(e => e.InstanceId == sagaContext.InstanceId);
        unLockedState.Should().NotBeNull();
        unLockedState.LockId.Should().BeNull();
        unLockedState.LockTime.Should().BeNull();
        unLockedState.IsCompleted.Should().BeTrue();
        unLockedState.ProcessedMessages.Should().NotBeNullOrEmpty()
                                       .And.HaveCount(2)
                                       .And.Contain(m => m.MessageId == messageContext.Id)
                                       .And.Contain(m => m.MessageId == messageContext2.Id);
    }

    private SqlSagaStateRepository CreateSut(SagaDbContext db,
        SqlSagaStateRepositoryOptions options = null)
    {
        var serializer = new JsonSerializer();
        var sut = new SqlSagaStateRepository(db, options ?? SqlSagaStateRepositoryOptions.Default, serializer);
        return sut;
    }

    private IMessageContext<TM> CreateMessageContext<TM>() where TM: IMessage
    {
        var messageContext = NSubstitute.Substitute.For<IMessageContext<TM>>();
        messageContext.Id.Returns(Guid.NewGuid().ToString());
        messageContext.CorrelationId.Returns(Guid.NewGuid().ToString());
        return messageContext;
    }

    private ISagaExecutionContext CreateSagaContext<TM>(IMessageContext<TM> messageContext)
        where TM : IMessage
    {
        var descriptor = SagaDescriptor.Create<FakeSagaNoState>();

        var factory = new SagaExecutionContextFactory();
        var context = factory.CreateState(descriptor, messageContext);

        return context;
    }
}
