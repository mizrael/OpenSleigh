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

    private static IMessageContext<FakeMessage> CreateMessageContext()
    {
        var messageContext = Substitute.For<IMessageContext<FakeMessage>>();
        messageContext.MessageId.Returns(Guid.NewGuid().ToString());
        messageContext.CorrelationId.Returns(Guid.NewGuid().ToString());
        messageContext.Message.Returns(new FakeMessage());
        return messageContext;
    }

    private static ISagaInstance CreateSaga(IMessageContext<FakeMessage> messageContext)
        => new SagaInstanceFactory().Create(SagaDescriptor.Create<FakeSagaNoState>(), messageContext);
}
