using Microsoft.Extensions.Logging;
using OpenSleigh.Outbox;
using OpenSleigh.Persistence;
using OpenSleigh.Transport;

namespace OpenSleigh.Tests.Outbox;

public class OutboxProcessorTests
{
    [Fact]
    public async Task ProcessPendingMessagesAsync_should_publish_and_delete_messages()
    {
        var outboxRepo = Substitute.For<IOutboxRepository>();
        var publisher = Substitute.For<IPublisher>();
        var logger = Substitute.For<ILogger<OutboxProcessor>>();
        var txManager = Substitute.For<ITransactionManager>();
        var tx = Substitute.For<ITransaction>();

        txManager.StartTransactionAsync(Arg.Any<CancellationToken>()).Returns(tx);

        var envelope = DummyMessage.CreateEnvelope();
        outboxRepo.ReadPendingAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { envelope });

        var sut = new OutboxProcessor(outboxRepo, publisher, logger, txManager);

        await sut.ProcessPendingMessagesAsync(CancellationToken.None);

        await publisher.Received(1).PublishAsync(envelope, Arg.Any<CancellationToken>());
        await outboxRepo.Received(1).DeleteAsync(envelope, Arg.Any<CancellationToken>());
        await tx.Received(1).CommitAsync();
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_should_skip_null_messages()
    {
        var outboxRepo = Substitute.For<IOutboxRepository>();
        var publisher = Substitute.For<IPublisher>();
        var logger = Substitute.For<ILogger<OutboxProcessor>>();
        var txManager = Substitute.For<ITransactionManager>();
        var tx = Substitute.For<ITransaction>();

        txManager.StartTransactionAsync(Arg.Any<CancellationToken>()).Returns(tx);

        outboxRepo.ReadPendingAsync(Arg.Any<CancellationToken>())
            .Returns(new MessageEnvelope?[] { null! });

        var sut = new OutboxProcessor(outboxRepo, publisher, logger, txManager);

        await sut.ProcessPendingMessagesAsync(CancellationToken.None);

        await publisher.DidNotReceiveWithAnyArgs().PublishAsync(default!, default);
        await tx.Received(1).CommitAsync();
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_should_handle_LockException_gracefully()
    {
        var outboxRepo = Substitute.For<IOutboxRepository>();
        var publisher = Substitute.For<IPublisher>();
        var logger = Substitute.For<ILogger<OutboxProcessor>>();
        var txManager = Substitute.For<ITransactionManager>();
        var tx = Substitute.For<ITransaction>();

        txManager.StartTransactionAsync(Arg.Any<CancellationToken>()).Returns(tx);

        var envelope = DummyMessage.CreateEnvelope();
        outboxRepo.ReadPendingAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { envelope });

        publisher.PublishAsync(envelope, Arg.Any<CancellationToken>())
            .Returns(x => throw new LockException("locked"));

        var sut = new OutboxProcessor(outboxRepo, publisher, logger, txManager);

        await sut.ProcessPendingMessagesAsync(CancellationToken.None);

        await outboxRepo.DidNotReceive().DeleteAsync(envelope, Arg.Any<CancellationToken>());
        await tx.Received(1).CommitAsync();
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_should_handle_general_exception_per_message()
    {
        var outboxRepo = Substitute.For<IOutboxRepository>();
        var publisher = Substitute.For<IPublisher>();
        var logger = Substitute.For<ILogger<OutboxProcessor>>();
        var txManager = Substitute.For<ITransactionManager>();
        var tx = Substitute.For<ITransaction>();

        txManager.StartTransactionAsync(Arg.Any<CancellationToken>()).Returns(tx);

        var envelope = DummyMessage.CreateEnvelope();
        outboxRepo.ReadPendingAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { envelope });

        publisher.PublishAsync(envelope, Arg.Any<CancellationToken>())
            .Returns(x => throw new InvalidOperationException("publish failed"));

        var sut = new OutboxProcessor(outboxRepo, publisher, logger, txManager);

        await sut.ProcessPendingMessagesAsync(CancellationToken.None);

        await outboxRepo.DidNotReceive().DeleteAsync(envelope, Arg.Any<CancellationToken>());
        await tx.Received(1).CommitAsync();
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_should_rollback_on_outer_exception()
    {
        var outboxRepo = Substitute.For<IOutboxRepository>();
        var publisher = Substitute.For<IPublisher>();
        var logger = Substitute.For<ILogger<OutboxProcessor>>();
        var txManager = Substitute.For<ITransactionManager>();
        var tx = Substitute.For<ITransaction>();

        txManager.StartTransactionAsync(Arg.Any<CancellationToken>()).Returns(tx);

        outboxRepo.ReadPendingAsync(Arg.Any<CancellationToken>())
            .Returns<IEnumerable<MessageEnvelope>>(x => throw new InvalidOperationException("db error"));

        var sut = new OutboxProcessor(outboxRepo, publisher, logger, txManager);

        await sut.ProcessPendingMessagesAsync(CancellationToken.None);

        await tx.Received(1).RollbackAsync();
    }

    [Fact]
    public void Ctor_should_throw_when_outboxRepository_is_null()
    {
        var publisher = Substitute.For<IPublisher>();
        var logger = Substitute.For<ILogger<OutboxProcessor>>();
        var txManager = Substitute.For<ITransactionManager>();
        Assert.Throws<ArgumentNullException>(() => new OutboxProcessor(null!, publisher, logger, txManager));
    }

    [Fact]
    public void Ctor_should_throw_when_publisher_is_null()
    {
        var outboxRepo = Substitute.For<IOutboxRepository>();
        var logger = Substitute.For<ILogger<OutboxProcessor>>();
        var txManager = Substitute.For<ITransactionManager>();
        Assert.Throws<ArgumentNullException>(() => new OutboxProcessor(outboxRepo, null!, logger, txManager));
    }

    [Fact]
    public void Ctor_should_throw_when_logger_is_null()
    {
        var outboxRepo = Substitute.For<IOutboxRepository>();
        var publisher = Substitute.For<IPublisher>();
        var txManager = Substitute.For<ITransactionManager>();
        Assert.Throws<ArgumentNullException>(() => new OutboxProcessor(outboxRepo, publisher, null!, txManager));
    }

    [Fact]
    public void Ctor_should_throw_when_transactionManager_is_null()
    {
        var outboxRepo = Substitute.For<IOutboxRepository>();
        var publisher = Substitute.For<IPublisher>();
        var logger = Substitute.For<ILogger<OutboxProcessor>>();
        Assert.Throws<ArgumentNullException>(() => new OutboxProcessor(outboxRepo, publisher, logger, null!));
    }
}
