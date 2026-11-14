using OpenSleigh.Transport;

namespace OpenSleigh.Tests;

public class SagaDescriptorTests
{
    [Fact]
    public void Create_should_fail_when_saga_has_no_initiator()
    {
        Assert.Throws<MissingMethodException>(SagaDescriptor.Create<FakeSagaNoStarter>);
    }

    [Fact]
    public void Create_should_return_valid_instance_when_input_valid()
    {
        var descriptor = SagaDescriptor.Create<FakeSagaWithState>();
        Assert.Contains(typeof(FakeSagaStarter), descriptor.InitiatorTypes);
        Assert.Equal(typeof(FakeSagaWithState), descriptor.SagaType);
        Assert.Null(descriptor.SagaStateType);
    }

    [Fact]
    public void CreateWithState_should_return_valid_instance_when_input_valid()
    {
        var descriptor = SagaDescriptor.Create<FakeSagaWithState, int>();
        Assert.Contains(typeof(FakeSagaStarter), descriptor.InitiatorTypes);
        Assert.Equal(typeof(FakeSagaWithState), descriptor.SagaType);
        Assert.Equal(typeof(int), descriptor.SagaStateType);
    }

    private class CompensatingSaga : Saga, IStartedBy<FakeSagaStarter>, IHandleMessage<FakeSagaMessage>, ICompensateMessage<FakeSagaMessage>
    {
        public CompensatingSaga(ISagaInstance context) : base(context) { }

        public ValueTask HandleAsync(IMessageContext<FakeSagaStarter> messageContext, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask HandleAsync(IMessageContext<FakeSagaMessage> messageContext, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask CompensateAsync(IMessageContext<FakeSagaMessage> messageContext, MessageFailure failure, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    private class SagaCompensatingWhatItDoesNotHandle : Saga, IStartedBy<FakeSagaStarter>, ICompensateMessage<FakeSagaMessage>
    {
        public SagaCompensatingWhatItDoesNotHandle(ISagaInstance context) : base(context) { }

        public ValueTask HandleAsync(IMessageContext<FakeSagaStarter> messageContext, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask CompensateAsync(IMessageContext<FakeSagaMessage> messageContext, MessageFailure failure, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    private class SagaWrittenForVersion3 : Saga, IStartedBy<FakeSagaStarter>
    {
        public SagaWrittenForVersion3(ISagaInstance context) : base(context) { }

        public ValueTask HandleAsync(IMessageContext<FakeSagaStarter> messageContext, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask RollbackAsync(IMessageContext<FakeSagaStarter> messageContext, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    private class SagaWithAnUnrelatedRollback : Saga, IStartedBy<FakeSagaStarter>
    {
        public SagaWithAnUnrelatedRollback(ISagaInstance context) : base(context) { }

        public ValueTask HandleAsync(IMessageContext<FakeSagaStarter> messageContext, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask RollbackAsync(string orderId, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    [Fact]
    public void Create_should_find_the_messages_the_saga_compensates()
    {
        var descriptor = SagaDescriptor.Create<CompensatingSaga>();

        Assert.Equal([typeof(FakeSagaMessage)], descriptor.CompensatedMessageTypes);
    }

    [Fact]
    public void Create_should_find_no_compensated_message_for_a_saga_without_compensation_handlers()
    {
        Assert.Empty(SagaDescriptor.Create<FakeSaga>().CompensatedMessageTypes);
    }

    [Fact]
    public void Create_should_fail_when_the_saga_compensates_a_message_it_does_not_handle()
    {
        var failure = Assert.Throws<ArgumentException>(SagaDescriptor.Create<SagaCompensatingWhatItDoesNotHandle>);

        Assert.Contains(typeof(FakeSagaMessage).FullName!, failure.Message);
    }

    [Fact]
    public void Create_should_fail_and_say_what_to_do_when_the_saga_still_declares_the_rollback_of_version_3()
    {
        var failure = Assert.Throws<InvalidOperationException>(SagaDescriptor.Create<SagaWrittenForVersion3>);

        Assert.Contains("RollbackAsync", failure.Message);
        Assert.Contains("ICompensateMessage<FakeSagaStarter>", failure.Message);
        Assert.Contains("CompensateAsync", failure.Message);
    }

    [Fact]
    public void Create_should_accept_a_saga_with_a_rollback_method_that_is_not_the_one_of_version_3()
    {
        Assert.Equal(typeof(SagaWithAnUnrelatedRollback), SagaDescriptor.Create<SagaWithAnUnrelatedRollback>().SagaType);
    }
}
