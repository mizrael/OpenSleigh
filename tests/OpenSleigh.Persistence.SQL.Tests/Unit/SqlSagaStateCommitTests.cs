using OpenSleigh.Outbox;
using OpenSleigh.Transport;
using OpenSleigh.Utils;
using System.Linq;

namespace OpenSleigh.Persistence.SQL.Tests.Unit;

/// <summary>
/// the commit of a saga on the in-memory EF Core provider: no server, so these run everywhere.
/// The same behaviour is checked against PostgreSQL and SQL Server by the integration tests.
/// </summary>
public class SqlSagaStateCommitTests
{
    [Fact]
    public async Task CommitAsync_should_store_the_outbox_and_release_the_saga_together()
    {
        var dbName = Guid.NewGuid().ToString();
        var sut = CreateSut(dbName);
        var messageContext = CreateMessageContext();
        var saga = CreateSaga(messageContext);
        await saga.LockAsync(sut, CancellationToken.None);
        saga.SetAsProcessed(messageContext);
        var published = Publish(saga);

        await sut.CommitAsync(saga);

        await using var db = CreateDbContext(dbName);
        var stored = await db.SagaStates.Include(e => e.ProcessedMessages).SingleAsync();
        Assert.Null(stored.LockId);
        Assert.Single(stored.ProcessedMessages);
        Assert.Equal(published.MessageId, (await db.OutboxMessages.SingleAsync()).MessageId);
    }

    [Fact]
    public async Task CommitAsync_should_fail_with_a_lock_exception_when_the_lock_was_taken_over()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new SqlSagaStateRepositoryOptions(TimeSpan.Zero);
        var firstOwnerRepository = CreateSut(dbName, options);
        var secondOwnerRepository = CreateSut(dbName, options);

        var messageContext = CreateMessageContext();
        var firstOwner = CreateSaga(messageContext);
        await firstOwner.LockAsync(firstOwnerRepository, CancellationToken.None);
        var secondOwner = new SagaInstance(firstOwner.InstanceId, firstOwner.TriggerMessageId, firstOwner.CorrelationId, firstOwner.Descriptor);
        await secondOwner.LockAsync(secondOwnerRepository, CancellationToken.None);

        firstOwner.SetAsProcessed(messageContext);
        Publish(firstOwner);
        await Assert.ThrowsAsync<LockException>(async () => await firstOwnerRepository.CommitAsync(firstOwner));

        // the in-memory provider has no transactions, so "nothing else was stored" is only checked
        // against the real databases, by the integration tests
        await using var db = CreateDbContext(dbName);
        Assert.Equal(secondOwner.LockId, (await db.SagaStates.SingleAsync()).LockId);
    }

    [Fact]
    public async Task ReleaseAsync_should_release_the_saga_after_a_commit_that_did_not_complete()
    {
        var dbName = Guid.NewGuid().ToString();
        var sut = CreateSut(dbName);
        var messageContext = CreateMessageContext();
        var saga = CreateSaga(messageContext);
        await saga.LockAsync(sut, CancellationToken.None);
        saga.SetAsProcessed(messageContext);
        Publish(saga);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await sut.CommitAsync(saga, cancelled.Token));

        // what SagaInstance does when its commit fails
        await sut.ReleaseAsync(saga);

        await using var db = CreateDbContext(dbName);
        Assert.Null((await db.SagaStates.SingleAsync()).LockId);
        Assert.Empty(await db.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task ReleaseAsync_should_not_store_the_outbox()
    {
        var dbName = Guid.NewGuid().ToString();
        var sut = CreateSut(dbName);
        var saga = CreateSaga(CreateMessageContext());
        await saga.LockAsync(sut, CancellationToken.None);
        Publish(saga);

        await sut.ReleaseAsync(saga);

        await using var db = CreateDbContext(dbName);
        Assert.Empty(await db.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task LockAsync_should_lock_a_saga_found_by_the_same_repository_and_release_should_add_only_the_new_message()
    {
        var dbName = Guid.NewGuid().ToString();
        var firstMessage = CreateMessageContext();
        var correlationId = firstMessage.CorrelationId;
        var saga = await StoreSagaAsync(dbName, firstMessage);

        var sut = CreateSut(dbName);
        var secondMessage = CreateMessageContext(correlationId);
        var found = await sut.FindAsync(saga.Descriptor, secondMessage);
        Assert.NotNull(found);
        await found.LockAsync(sut, CancellationToken.None);

        await using (var db = CreateDbContext(dbName))
            Assert.Equal(found.LockId, (await db.SagaStates.SingleAsync()).LockId);

        found.SetAsProcessed(secondMessage);
        await sut.ReleaseAsync(found);

        await using var verificationDb = CreateDbContext(dbName);
        var stored = await verificationDb.SagaStates.Include(e => e.ProcessedMessages).SingleAsync();
        Assert.Null(stored.LockId);
        Assert.Equal(
            new[] { firstMessage.MessageId, secondMessage.MessageId }.Order(),
            stored.ProcessedMessages.Select(m => m.MessageId).Order());
    }

    [Fact]
    public async Task LockAsync_should_throw_when_the_saga_was_locked_by_someone_else_after_it_was_found()
    {
        var dbName = Guid.NewGuid().ToString();
        var firstMessage = CreateMessageContext();
        var saga = await StoreSagaAsync(dbName, firstMessage);

        var sut = CreateSut(dbName);
        var found = await sut.FindAsync(saga.Descriptor, CreateMessageContext(firstMessage.CorrelationId));
        Assert.NotNull(found);

        var someoneElse = CreateSut(dbName);
        await saga.LockAsync(someoneElse, CancellationToken.None);

        await Assert.ThrowsAsync<LockException>(async () => await found.LockAsync(sut, CancellationToken.None));

        await using (var db = CreateDbContext(dbName))
            Assert.Equal(saga.LockId, (await db.SagaStates.SingleAsync()).LockId);

        // and the same repository can lock it once it is released
        await someoneElse.ReleaseAsync(saga);
        await found.LockAsync(sut, CancellationToken.None);

        await using var dbAfterRelease = CreateDbContext(dbName);
        Assert.Equal(found.LockId, (await dbAfterRelease.SagaStates.SingleAsync()).LockId);
    }

    [Fact]
    public async Task LockAsync_should_lock_a_saga_that_was_locked_when_found_and_released_since()
    {
        var dbName = Guid.NewGuid().ToString();
        var firstMessage = CreateMessageContext();
        var saga = await StoreSagaAsync(dbName, firstMessage);

        var someoneElse = CreateSut(dbName);
        await saga.LockAsync(someoneElse, CancellationToken.None);

        var sut = CreateSut(dbName);
        var found = await sut.FindAsync(saga.Descriptor, CreateMessageContext(firstMessage.CorrelationId));
        Assert.NotNull(found);

        await someoneElse.ReleaseAsync(saga);

        await found.LockAsync(sut, CancellationToken.None);

        await using var db = CreateDbContext(dbName);
        Assert.Equal(found.LockId, (await db.SagaStates.SingleAsync()).LockId);
    }

    private static async Task<ISagaInstance> StoreSagaAsync(string dbName, IMessageContext<FakeMessage> messageContext)
    {
        var repository = CreateSut(dbName);
        var saga = CreateSaga(messageContext);
        await saga.LockAsync(repository, CancellationToken.None);
        saga.SetAsProcessed(messageContext);
        await repository.ReleaseAsync(saga);
        return saga;
    }

    private static MessageEnvelope Publish(ISagaInstance saga)
    {
        var envelope = MessageEnvelope.Create(new FakeMessage(), saga);
        saga.Publish(envelope);
        return envelope;
    }

    private static SqlSagaStateRepository CreateSut(string dbName, SqlSagaStateRepositoryOptions? options = null)
        => new(CreateDbContext(dbName), options ?? SqlSagaStateRepositoryOptions.Default, new JsonSerializer());

    private static SagaDbContext CreateDbContext(string dbName)
        => new(new DbContextOptionsBuilder<SagaDbContext>().UseInMemoryDatabase(dbName).Options);

    private static IMessageContext<FakeMessage> CreateMessageContext(string? correlationId = null)
    {
        var messageContext = Substitute.For<IMessageContext<FakeMessage>>();
        messageContext.MessageId.Returns(Guid.NewGuid().ToString());
        messageContext.CorrelationId.Returns(correlationId ?? Guid.NewGuid().ToString());
        messageContext.Message.Returns(new FakeMessage());
        return messageContext;
    }

    private static ISagaInstance CreateSaga(IMessageContext<FakeMessage> messageContext)
        => new SagaInstanceFactory().Create(SagaDescriptor.Create<FakeSagaNoState>(), messageContext);
}
