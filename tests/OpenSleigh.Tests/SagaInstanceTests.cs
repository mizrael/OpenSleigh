using FluentAssertions;

namespace OpenSleigh.Tests;

public class SagaInstanceTests
{
    [Fact]
    public void Constructor_should_initialize_properties()
    {
        var processedMessages = new ProcessedMessage[]
        {
            ProcessedMessage.Create(FakeMessageContext<FakeSagaStarter>.Create(new FakeSagaStarter())),
            ProcessedMessage.Create(FakeMessageContext<FakeSagaStarter>.Create(new FakeSagaStarter()))
        };
        var descriptor = SagaDescriptor.Create<FakeSaga>();
        var sut = new SagaInstance("lorem", "ipsum", "dolor", descriptor, processedMessages);
        sut.InstanceId.Should().Be("lorem");
        sut.CorrelationId.Should().Be("dolor");
        sut.Descriptor.Should().Be(descriptor);
        sut.ProcessedMessages.Should().BeEquivalentTo(processedMessages);
    }

    [Fact]
    public void Constructor_should_throw_when_descriptor_is_null()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new SagaInstance("lorem", "ipsum", "dolor", null!));
        ex.ParamName.Should().Be("descriptor");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_should_throw_when_instance_id_is_null_or_empty(string instanceId)
    {
        var ex = Assert.ThrowsAny<ArgumentException>(() => new SagaInstance(instanceId, "ipsum", "dolor", SagaDescriptor.Create<FakeSaga>()));
        ex.ParamName.Should().Be("instanceId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_should_throw_when_correlation_id_is_null_or_empty(string correlationId)
    {
        var ex = Assert.ThrowsAny<ArgumentException>(() => new SagaInstance("baz", "ipsum", correlationId, SagaDescriptor.Create<FakeSaga>()));
        ex.ParamName.Should().Be("correlationId");
    }

    [Fact]
    public void Constructor_should_set_State_when_provided()
    {
        var processedMessages = new ProcessedMessage[]
       {
            ProcessedMessage.Create(FakeMessageContext<FakeSagaStarter>.Create(new FakeSagaStarter())),
            ProcessedMessage.Create(FakeMessageContext<FakeSagaStarter>.Create(new FakeSagaStarter()))
       };
        var descriptor = SagaDescriptor.Create<FakeSaga>();
        var sut = new SagaInstance<string>("lorem", "ipsum", "dolor", descriptor, state: "lorem ipsum", processedMessages);

        Assert.Equal("lorem ipsum", sut.State);
    }

    public void CanProcess_should_return_true_when_message_not_processed()
    {
        var descriptor = SagaDescriptor.Create<FakeSaga>();

        var message = new FakeSagaStarter();

        var messageContext = FakeMessageContext<FakeSagaStarter>.Create(message);

        var sut = new SagaInstance("lorem", "ipsum", messageContext.CorrelationId, descriptor);

        sut.CanProcess(messageContext).Should().BeTrue();
    }

    [Fact]
    public void CanProcess_should_return_false_when_saga_completed()
    {
        var descriptor = SagaDescriptor.Create<FakeSaga>();

        var message = new FakeSagaStarter();
        var messageContext = FakeMessageContext<FakeSagaStarter>.Create(message);

        var sut = new SagaInstance("lorem", "ipsum", messageContext.CorrelationId, descriptor);
        sut.MarkAsCompleted();

        sut.CanProcess(messageContext).Should().BeFalse();
    }

    [Fact]
    public void CanProcess_should_return_true_when_correlation_different()
    {
        var descriptor = SagaDescriptor.Create<FakeSaga>();

        var message = new FakeSagaStarter();

        var messageContext = FakeMessageContext<FakeSagaStarter>.Create(message);

        var sut = new SagaInstance("lorem", "ipsum", Guid.NewGuid().ToString(), descriptor);

        sut.CanProcess(messageContext).Should().BeFalse();
    }

    [Fact]
    public void CanProcess_should_return_true_when_message_from_another_saga_and_initiator()
    {
        var descriptor = SagaDescriptor.Create<FakeSaga>();

        var messageContext = FakeMessageContext<FakeSagaStarter>.Create(
            new FakeSagaStarter(),
            senderId: Guid.NewGuid().ToString());

        var sut = new SagaInstance("lorem", "ipsum", messageContext.CorrelationId, descriptor);
        sut.CanProcess(messageContext).Should().BeTrue();
    }

    [Fact]
    public void CanProcess_should_return_true_when_parent_message_processed_and_sender_is_instance()
    {
        var descriptor = SagaDescriptor.Create<FakeSaga>();

        var correlationId = Guid.NewGuid().ToString();

        var sut = new SagaInstance("lorem", "ipsum", correlationId, descriptor);

        var parentMessageContext = FakeMessageContext<FakeSagaMessage>.Create(
            new FakeSagaMessage(),
            correlationId: correlationId,
            parentId: Guid.NewGuid().ToString(),
            senderId: sut.InstanceId);

        sut.SetAsProcessed(parentMessageContext);

        var messageContext = FakeMessageContext<FakeSagaMessage>.Create(
            new FakeSagaMessage(),
            correlationId: correlationId,
            parentId: parentMessageContext.MessageId,
            senderId: sut.InstanceId);

        sut.CanProcess(messageContext).Should().BeTrue();
    }

    [Fact]
    public void CanProcess_should_return_false_when_sender_is_not_instance_and_message_not_initiator_and_correlation_different()
    {
        var descriptor = SagaDescriptor.Create<FakeSaga>();

        var messageContext = FakeMessageContext<FakeSagaMessage>.Create(
            new FakeSagaMessage(),
            senderId: Guid.NewGuid().ToString());

        var sut = new SagaInstance("lorem", "ipsum", correlationId: Guid.NewGuid().ToString(), descriptor);

        sut.CanProcess(messageContext).Should().BeFalse();
    }

    [Fact]
    public void CanProcess_should_return_true_when_sender_is_not_instance_and_message_not_initiator_and_same_correlation()
    {
        var descriptor = SagaDescriptor.Create<FakeSaga>();

        var messageContext = FakeMessageContext<FakeSagaMessage>.Create(
            new FakeSagaMessage(),
            senderId: Guid.NewGuid().ToString());

        var sut = new SagaInstance("lorem", "ipsum", messageContext.CorrelationId, descriptor);

        sut.CanProcess(messageContext).Should().BeTrue();
    }

    [Fact]
    public void CanProcess_should_return_false_when_message_already_processed()
    {
        var descriptor = SagaDescriptor.Create<FakeSaga>();

        var message = new FakeSagaStarter();
        var messageContext = FakeMessageContext<FakeSagaStarter>.Create(message);

        var sut = new SagaInstance("lorem", "ipsum", messageContext.CorrelationId, descriptor);
        sut.SetAsProcessed(messageContext);

        sut.CanProcess(messageContext).Should().BeFalse();
    }

    [Fact]
    public void CanProcess_should_return_false_when_same_idempotent_message_already_processed()
    {
        var descriptor = SagaDescriptor.Create<FakeSaga>();

        var message = new FakeIdempotentMessage("test key");
        var messageContext = FakeMessageContext<FakeIdempotentMessage>.Create(message);

        var sut = new SagaInstance("lorem", "ipsum", messageContext.CorrelationId, descriptor);
        sut.SetAsProcessed(messageContext);

        Assert.False(sut.CanProcess(messageContext));
    }

    [Fact]
    public void SetAsProcessed_should_throw_when_same_idempotent_message_already_processed()
    {
        var descriptor = SagaDescriptor.Create<FakeSaga>();

        var message = new FakeIdempotentMessage("test key");
        var messageContext = FakeMessageContext<FakeIdempotentMessage>.Create(message);

        var sut = new SagaInstance("lorem", "ipsum", messageContext.CorrelationId, descriptor);
        sut.SetAsProcessed(messageContext);

        Assert.ThrowsAny<InvalidOperationException>(() => sut.SetAsProcessed(messageContext));
    }

    [Fact]
    public void CanProcess_should_return_false_when_idempotent_message_already_processed()
    {
        var idempotencyKey = "test key";
        var descriptor = SagaDescriptor.Create<FakeSaga>();

        var message = new FakeIdempotentMessage(idempotencyKey);
        var messageContext = FakeMessageContext<FakeIdempotentMessage>.Create(message);

        var sut = new SagaInstance("lorem", "ipsum", messageContext.CorrelationId, descriptor);
        sut.SetAsProcessed(messageContext);

        var message2 = new FakeIdempotentMessage(idempotencyKey);
        var messageContext2 = FakeMessageContext<FakeIdempotentMessage>.Create(message2);
        Assert.False(sut.CanProcess(messageContext2));
    }

    [Fact]
    public void SetAsProcessed_should_throw_when_idempotent_message_already_processed()
    {
        var idempotencyKey = "test key";
        var descriptor = SagaDescriptor.Create<FakeSaga>();

        var message = new FakeIdempotentMessage(idempotencyKey);
        var messageContext = FakeMessageContext<FakeIdempotentMessage>.Create(message);

        var sut = new SagaInstance("lorem", "ipsum", messageContext.CorrelationId, descriptor);
        sut.SetAsProcessed(messageContext);

        var message2 = new FakeIdempotentMessage(idempotencyKey);
        var messageContext2 = FakeMessageContext<FakeIdempotentMessage>.Create(message2);
        Assert.ThrowsAny<InvalidOperationException>(() => sut.SetAsProcessed(messageContext2));
    }
}