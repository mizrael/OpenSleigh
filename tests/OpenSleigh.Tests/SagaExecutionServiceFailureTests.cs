using OpenSleigh.Outbox;
using OpenSleigh.Transport;
using System.Collections.Generic;

namespace OpenSleigh.Tests;

public class SagaExecutionServiceFailureTests
{
    private class SagaWithoutCompensation : Saga, IStartedBy<FakeSagaStarter>
    {
        public SagaWithoutCompensation(ISagaInstance context) : base(context) { }

        public ValueTask HandleAsync(IMessageContext<FakeSagaStarter> messageContext, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    private class SagaWithCompensation : Saga, IStartedBy<FakeSagaStarter>, ICompensateMessage<FakeSagaStarter>
    {
        public SagaWithCompensation(ISagaInstance context) : base(context) { }

        public ValueTask HandleAsync(IMessageContext<FakeSagaStarter> messageContext, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask CompensateAsync(IMessageContext<FakeSagaStarter> messageContext, MessageFailure failure, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    private const string LockId = "lock-123";

    private readonly ISagaStateRepository _repository = Substitute.For<ISagaStateRepository>();
    private readonly IOutboxRepository _outbox = Substitute.For<IOutboxRepository>();
    private readonly IMessageHandlerManager _handlerManager = Substitute.For<IMessageHandlerManager>();
    private readonly FakeMessageContext<FakeSagaStarter> _messageContext = FakeMessageContext<FakeSagaStarter>.Create(new FakeSagaStarter());
    private readonly InvalidOperationException _handlerError = new("out of stock");
    private readonly SagaException _failure;

    // what the repository was asked to store, as it was at that moment
    private readonly List<(string Operation, ISagaInstance Instance, SagaMessageFailure? Failure, int Outbox, int Processed)> _stored = new();

    public SagaExecutionServiceFailureTests()
    {
        _failure = new SagaException("the handler failed", _handlerError);

        _repository.When(x => x.AbandonAsync(Arg.Any<ISagaInstance>(), Arg.Any<CancellationToken>()))
                   .Do(call => Remember(nameof(ISagaStateRepository.AbandonAsync), call.Arg<ISagaInstance>()));
        _repository.When(x => x.ReleaseAsync(Arg.Any<ISagaInstance>(), Arg.Any<CancellationToken>()))
                   .Do(call => Remember(nameof(ISagaStateRepository.ReleaseAsync), call.Arg<ISagaInstance>()));
    }

    private void Remember(string operation, ISagaInstance instance)
        => _stored.Add((operation, instance, instance.Failure, instance.Outbox.Count, instance.ProcessedMessages.Count));

    private SagaExecutionService CreateSut()
        => new(Substitute.For<ISagaInstanceFactory>(), _repository, _outbox);

    private async Task<SagaInstance> CreateLockedInstanceAsync<TSaga>() where TSaga : ISaga
    {
        var instance = NewInstance<TSaga>("instance-1");
        var locker = Substitute.For<ISagaStateRepository>();
        locker.LockAsync(instance, Arg.Any<CancellationToken>()).Returns(LockId);
        await instance.LockAsync(locker, CancellationToken.None);

        // what a failed handler leaves behind
        instance.Publish(DummyMessage.CreateEnvelope());
        instance.MarkAsCompleted();
        return instance;
    }

    private SagaInstance NewInstance<TSaga>(string instanceId) where TSaga : ISaga
        => new(instanceId, "trigger-1", _messageContext.CorrelationId, SagaDescriptor.Create<TSaga>());

    private SagaInstance StoredInstanceIsFound<TSaga>() where TSaga : ISaga
    {
        var stored = NewInstance<TSaga>("instance-1");
        _repository.FindAsync(Arg.Any<SagaDescriptor>(), _messageContext, Arg.Any<CancellationToken>())
                   .Returns(stored);
        return stored;
    }

    [Fact]
    public async Task A_message_without_compensation_handler_should_be_recorded_as_dead_lettered_and_the_saga_abandoned()
    {
        var failed = await CreateLockedInstanceAsync<SagaWithoutCompensation>();

        var thrown = await Assert.ThrowsAsync<SagaException>(
            () => CreateSut().HandleFailureAsync(failed, _messageContext, _handlerManager, _failure).AsTask());

        Assert.Same(_failure, thrown);

        var stored = Assert.Single(_stored);
        Assert.Equal(nameof(ISagaStateRepository.AbandonAsync), stored.Operation);
        Assert.Same(failed, stored.Instance);
        Assert.Equal(0, stored.Outbox);

        Assert.NotNull(stored.Failure);
        Assert.Equal(FailureDecision.DeadLettered, stored.Failure.Decision);
        Assert.Equal(1, stored.Failure.Attempt);
        Assert.Equal("instance-1", stored.Failure.InstanceId);
        Assert.Equal(_messageContext.CorrelationId, stored.Failure.CorrelationId);
        Assert.Equal(_messageContext.MessageId, stored.Failure.MessageId);
        Assert.Equal(typeof(FakeSagaStarter).FullName, stored.Failure.MessageType);
        Assert.Equal(typeof(SagaWithoutCompensation).FullName, stored.Failure.SagaType);
        Assert.Equal(typeof(InvalidOperationException).FullName, stored.Failure.ExceptionType);
        Assert.Equal("out of stock", stored.Failure.ExceptionMessage);
        Assert.Null(stored.Failure.CompensationError);

        await _handlerManager.DidNotReceiveWithAnyArgs().CompensateAsync<FakeSagaStarter>(default!, default!, default!, default);
        await _outbox.DidNotReceiveWithAnyArgs().AppendAsync(default!, default);
    }

    [Fact]
    public async Task The_failure_should_still_be_thrown_when_the_saga_cannot_be_abandoned()
    {
        var failed = await CreateLockedInstanceAsync<SagaWithoutCompensation>();
        _repository.AbandonAsync(failed, Arg.Any<CancellationToken>())
                   .Returns(_ => ValueTask.FromException(new InvalidOperationException("the database is gone")));

        var thrown = await Assert.ThrowsAsync<SagaException>(
            () => CreateSut().HandleFailureAsync(failed, _messageContext, _handlerManager, _failure).AsTask());

        Assert.Same(_failure, thrown);
    }

    [Fact]
    public async Task A_compensated_message_should_be_stored_as_processed_on_the_saga_as_it_was_before_the_handler()
    {
        var failed = await CreateLockedInstanceAsync<SagaWithCompensation>();
        var asStored = StoredInstanceIsFound<SagaWithCompensation>();
        MessageFailure? toldToCompensation = null;
        _handlerManager.CompensateAsync(asStored, _messageContext, Arg.Any<MessageFailure>(), Arg.Any<CancellationToken>())
                       .Returns(call =>
                       {
                           toldToCompensation = call.Arg<MessageFailure>();
                           asStored.Publish(DummyMessage.CreateEnvelope());
                           return ValueTask.CompletedTask;
                       });
        // counted when they are appended: the outbox of the instance is emptied once they are stored
        var appended = new List<int>();
        _outbox.AppendAsync(Arg.Do<IEnumerable<MessageEnvelope>>(messages => appended.Add(messages.Count())), Arg.Any<CancellationToken>())
               .Returns(OutboxAppendResult.Success);

        await CreateSut().HandleFailureAsync(failed, _messageContext, _handlerManager, _failure);

        Assert.Same(_handlerError, toldToCompensation!.Exception);
        Assert.Equal(1, toldToCompensation.Attempts);

        // the message the compensation published goes to the outbox, the one of the failed handler does not
        Assert.Equal([1], appended);

        var stored = Assert.Single(_stored);
        Assert.Equal(nameof(ISagaStateRepository.ReleaseAsync), stored.Operation);
        Assert.Same(asStored, stored.Instance);
        Assert.Equal(1, stored.Processed);
        Assert.Equal(FailureDecision.Compensated, stored.Failure!.Decision);
        Assert.Equal("out of stock", stored.Failure.ExceptionMessage);

        Assert.Equal(LockId, asStored.LockId);
        Assert.False(asStored.IsCompleted);
        Assert.Empty(failed.LockId);
    }

    [Fact]
    public async Task A_failed_compensation_should_be_recorded_and_the_saga_abandoned_as_it_was()
    {
        var failed = await CreateLockedInstanceAsync<SagaWithCompensation>();
        var asStored = StoredInstanceIsFound<SagaWithCompensation>();
        var compensationError = new TimeoutException("the warehouse did not answer");
        _handlerManager.CompensateAsync(asStored, _messageContext, Arg.Any<MessageFailure>(), Arg.Any<CancellationToken>())
                       .Returns(_ =>
                       {
                           asStored.Publish(DummyMessage.CreateEnvelope());
                           return ValueTask.FromException(compensationError);
                       });

        var thrown = await Assert.ThrowsAsync<SagaException>(
            () => CreateSut().HandleFailureAsync(failed, _messageContext, _handlerManager, _failure).AsTask());

        var both = Assert.IsType<AggregateException>(thrown.InnerException);
        Assert.Equal([_handlerError, compensationError], both.InnerExceptions);

        var stored = Assert.Single(_stored);
        Assert.Equal(nameof(ISagaStateRepository.AbandonAsync), stored.Operation);
        Assert.Same(asStored, stored.Instance);
        Assert.Equal(0, stored.Outbox);
        Assert.Equal(0, stored.Processed);
        Assert.Equal(FailureDecision.CompensationFailed, stored.Failure!.Decision);
        Assert.Equal("out of stock", stored.Failure.ExceptionMessage);
        Assert.Contains("the warehouse did not answer", stored.Failure.CompensationError);

        await _outbox.DidNotReceiveWithAnyArgs().AppendAsync(default!, default);
    }

    [Fact]
    public async Task A_saga_that_is_not_stored_any_more_should_be_recorded_as_a_failed_compensation()
    {
        var failed = await CreateLockedInstanceAsync<SagaWithCompensation>();
        _repository.FindAsync(Arg.Any<SagaDescriptor>(), _messageContext, Arg.Any<CancellationToken>())
                   .Returns((ISagaInstance?)null);

        await Assert.ThrowsAsync<SagaException>(
            () => CreateSut().HandleFailureAsync(failed, _messageContext, _handlerManager, _failure).AsTask());

        var stored = Assert.Single(_stored);
        Assert.Same(failed, stored.Instance);
        Assert.Equal(FailureDecision.CompensationFailed, stored.Failure!.Decision);
    }

    [Fact]
    public async Task A_compensation_interrupted_by_a_stop_should_abandon_the_saga_without_a_record()
    {
        var failed = await CreateLockedInstanceAsync<SagaWithCompensation>();
        var asStored = StoredInstanceIsFound<SagaWithCompensation>();
        using var stopping = new CancellationTokenSource();
        _handlerManager.CompensateAsync(asStored, _messageContext, Arg.Any<MessageFailure>(), Arg.Any<CancellationToken>())
                       .Returns(_ =>
                       {
                           stopping.Cancel();
                           return ValueTask.FromException(new OperationCanceledException(stopping.Token));
                       });

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => CreateSut().HandleFailureAsync(failed, _messageContext, _handlerManager, _failure, stopping.Token).AsTask());

        var stored = Assert.Single(_stored);
        Assert.Equal(nameof(ISagaStateRepository.AbandonAsync), stored.Operation);
        Assert.Same(asStored, stored.Instance);
        Assert.Null(stored.Failure);
    }

    [Fact]
    public async Task A_compensation_whose_lock_was_lost_should_not_be_recorded_and_should_be_reported_as_a_lock_conflict()
    {
        var failed = await CreateLockedInstanceAsync<SagaWithCompensation>();
        var asStored = StoredInstanceIsFound<SagaWithCompensation>();
        _repository.ReleaseAsync(asStored, Arg.Any<CancellationToken>())
                   .Returns(_ => ValueTask.FromException(new LockException("taken over")));

        var thrown = await Assert.ThrowsAsync<LockException>(
            () => CreateSut().HandleFailureAsync(failed, _messageContext, _handlerManager, _failure).AsTask());

        Assert.True(thrown.IsLockConflict());
        var abandoned = Assert.Single(_stored, stored => stored.Operation == nameof(ISagaStateRepository.AbandonAsync));
        Assert.Null(abandoned.Failure);
    }
}
