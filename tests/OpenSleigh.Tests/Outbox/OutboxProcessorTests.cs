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
    public async Task RelayPendingMessagesAsync_should_return_the_number_of_messages_relayed()
    {
        var outboxRepo = Substitute.For<IOutboxRepository>();
        var publisher = Substitute.For<IPublisher>();
        var txManager = Substitute.For<ITransactionManager>();
        txManager.StartTransactionAsync(Arg.Any<CancellationToken>()).Returns(Substitute.For<ITransaction>());

        var relayed = DummyMessage.CreateEnvelope();
        var failing = DummyMessage.CreateEnvelope();
        outboxRepo.ReadPendingAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { relayed, failing });
        publisher.PublishAsync(failing, Arg.Any<CancellationToken>())
            .Returns(x => throw new InvalidOperationException("publish failed"));

        var sut = new OutboxProcessor(outboxRepo, publisher, Substitute.For<ILogger<OutboxProcessor>>(), txManager);

        var result = await sut.RelayPendingMessagesAsync(CancellationToken.None);

        Assert.Equal(new OutboxRelayResult(Relayed: 1, Failed: 1), result);
    }

    [Fact]
    public async Task RelayPendingMessagesAsync_should_return_zero_when_the_transaction_is_rolled_back()
    {
        var outboxRepo = Substitute.For<IOutboxRepository>();
        var txManager = Substitute.For<ITransactionManager>();
        var tx = Substitute.For<ITransaction>();
        txManager.StartTransactionAsync(Arg.Any<CancellationToken>()).Returns(tx);
        tx.When(t => t.CommitAsync(Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("commit failed"));

        var envelope = DummyMessage.CreateEnvelope();
        outboxRepo.ReadPendingAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { envelope });

        var sut = new OutboxProcessor(outboxRepo, Substitute.For<IPublisher>(), Substitute.For<ILogger<OutboxProcessor>>(), txManager);

        var result = await sut.RelayPendingMessagesAsync(CancellationToken.None);

        Assert.Equal(new OutboxRelayResult(Relayed: 0, Failed: 1), result);
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
