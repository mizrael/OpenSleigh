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
    public async Task LockAsync_should_handle_multiple_saga_types_with_same_correlation_id()
    {
        var (db, _) = _fixture.CreateDbContext();
        var sut = CreateSut(db);

        // Use the same correlation ID for both sagas
        var sharedCorrelationId = Guid.NewGuid().ToString();

        // Create and lock first saga type (FakeSagaNoState) with shared correlation ID
        var messageContext1 = NSubstitute.Substitute.For<IMessageContext<FakeMessage>>();
        messageContext1.MessageId.Returns(Guid.NewGuid().ToString());
        messageContext1.CorrelationId.Returns(sharedCorrelationId);

        var descriptor1 = SagaDescriptor.Create<FakeSagaNoState>();
        var factory = new SagaInstanceFactory();
        var saga1 = factory.Create(descriptor1, messageContext1);

        var lockId1 = await sut.LockAsync(saga1, CancellationToken.None);
        lockId1.Should().NotBeNullOrEmpty();

        // Create and lock second saga type (FakeSagaWithState) with SAME correlation ID
        var messageContext2 = NSubstitute.Substitute.For<IMessageContext<FakeMessage>>();
        messageContext2.MessageId.Returns(Guid.NewGuid().ToString());
        messageContext2.CorrelationId.Returns(sharedCorrelationId);

        var descriptor2 = SagaDescriptor.Create<FakeSagaWithState>();
        var saga2 = factory.Create(descriptor2, messageContext2);

        // This should succeed - different saga types can share correlation IDs
        // But will fail with current implementation because line 96 doesn't filter by saga type
        var lockId2 = await sut.LockAsync(saga2, CancellationToken.None);
        lockId2.Should().NotBeNullOrEmpty();

        // Verify both sagas were created with different instance IDs
        saga1.InstanceId.Should().NotBe(saga2.InstanceId);

        // Verify both sagas exist in database with same correlation ID but different saga types
        var saga1Entity = await db.SagaStates.FirstOrDefaultAsync(e => e.InstanceId == saga1.InstanceId);
        var saga2Entity = await db.SagaStates.FirstOrDefaultAsync(e => e.InstanceId == saga2.InstanceId);

        saga1Entity.Should().NotBeNull();
        saga2Entity.Should().NotBeNull();
        saga1Entity!.CorrelationId.Should().Be(sharedCorrelationId);
        saga2Entity!.CorrelationId.Should().Be(sharedCorrelationId);
        saga1Entity.SagaType.Should().NotBe(saga2Entity.SagaType);
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

        var fakeContext = NSubstitute.Substitute.For<ISagaInstance >();
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
                                       .And.Contain(m => m.MessageId == messageContext.MessageId)
                                       .And.Contain(m => m.MessageId == messageContext2.MessageId);
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
        messageContext.MessageId.Returns(Guid.NewGuid().ToString());
        messageContext.CorrelationId.Returns(Guid.NewGuid().ToString());
        return messageContext;
    }

    private ISagaInstance  CreateSagaContext<TM>(IMessageContext<TM> messageContext)
        where TM : IMessage
    {
        var descriptor = SagaDescriptor.Create<FakeSagaNoState>();

        var factory = new SagaInstanceFactory();
        var context = factory.Create(descriptor, messageContext);

        return context;
    }
}
