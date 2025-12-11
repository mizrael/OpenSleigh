using FluentAssertions;
using OpenSleigh.Transport;

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

    [Fact]
    public void CanProcess_should_return_true_when_message_not_processed()
    {
        var descriptor = SagaDescriptor.Create<FakeSaga>();

        var message = new FakeSagaStarter();

        var messageContext = FakeMessageContext<FakeSagaStarter>.Create(message);

        var sut = new SagaInstance("lorem", "ipsum", messageContext.CorrelationId, descriptor);

        sut.CanProcess(messageContext).Should().BeTrue();
    }

    [Fact]
    public async Task CanProcess_should_return_true_when_saga_started_and_message_is_starter()
    {
        var descriptor = SagaDescriptor.Create<FakeSaga>();
        var starter = new FakeSagaStarter();
        var otherStarter = new OtherFakeSagaStarter();
        var starterContext = FakeMessageContext<FakeSagaStarter>.Create(starter);
        var otherStarterContext =
            FakeMessageContext<OtherFakeSagaStarter>.Create(otherStarter, correlationId: starterContext.CorrelationId);

        var handler = Substitute.For<IMessageHandlerManager>();
        var executionService = Substitute.For<ISagaExecutionService>();

        var sut = new SagaInstance("lorem", "ipsum", starterContext.CorrelationId, descriptor);
        await sut.ProcessAsync(handler, starterContext, executionService);

        sut.CanProcess(otherStarterContext).Should().BeTrue();
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
    public void CanProcess_should_return_false_when_correlation_different()
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
    public void CanProcess_should_return_false_when_idempotent_message_already_processed()
    {
        var messageId = "test key";
        var descriptor = SagaDescriptor.Create<FakeSaga>();

        var messageContext = Substitute.For<IMessageContext<DummyMessage>>();
        messageContext.MessageId.Returns(messageId);
        messageContext.CorrelationId.Returns(Guid.NewGuid().ToString());

        var sut = new SagaInstance("lorem", "ipsum", messageContext.CorrelationId, descriptor);
        sut.SetAsProcessed(messageContext);

        Assert.False(sut.CanProcess(messageContext));
    }

    [Fact]
    public void SetAsProcessed_should_throw_when_idempotent_message_already_processed()
    {
        var messageId = "test key";
        var descriptor = SagaDescriptor.Create<FakeSaga>();

        var messageContext = Substitute.For<IMessageContext<DummyMessage>>();
        messageContext.MessageId.Returns(messageId);
        messageContext.CorrelationId.Returns(Guid.NewGuid().ToString());

        var sut = new SagaInstance("lorem", "ipsum", messageContext.CorrelationId, descriptor);
        sut.SetAsProcessed(messageContext);

        var messageContext2 = Substitute.For<IMessageContext<DummyMessage>>();
        messageContext2.MessageId.Returns(messageId);
        Assert.ThrowsAny<InvalidOperationException>(() => sut.SetAsProcessed(messageContext2));
    }

    [Fact]
    public async Task ProcessAsync_should_process_any_associated_message()
    {
        var descriptor = SagaDescriptor.Create<FakeSaga>();
        var starter = new FakeSagaStarter();
        var otherStarter = new OtherFakeSagaStarter();
        var message = new FakeSagaMessage();
        var starterContext = FakeMessageContext<FakeSagaStarter>.Create(starter);
        var otherStarterContext =
            FakeMessageContext<OtherFakeSagaStarter>.Create(otherStarter, correlationId: starterContext.CorrelationId);
        var messageContext =
            FakeMessageContext<FakeSagaMessage>.Create(message, correlationId: starterContext.CorrelationId);

        var handler = Substitute.For<IMessageHandlerManager>();
        var executionService = Substitute.For<ISagaExecutionService>();

        var sut = new SagaInstance("lorem", "ipsum", starterContext.CorrelationId, descriptor);
        await sut.ProcessAsync(handler, starterContext, executionService);
        await sut.ProcessAsync(handler, otherStarterContext, executionService);
        await sut.ProcessAsync(handler, messageContext, executionService);
        await handler.Received(1).ProcessAsync(sut, starterContext, Arg.Any<CancellationToken>());
        await handler.Received(1).ProcessAsync(sut, otherStarterContext, Arg.Any<CancellationToken>());
        await handler.Received(1).ProcessAsync(sut, messageContext, Arg.Any<CancellationToken>());
        await executionService.Received(3).CommitAsync(sut, Arg.Any<CancellationToken>());

        sut.LockId.Should().BeEmpty();
        sut.ProcessedMessages.Should()
            .OnlyContain(pm => new []
            {
                starterContext.MessageId,
                otherStarterContext.MessageId,
                messageContext.MessageId
            }.Contains(pm.MessageId));
    }

    [Fact]
    public void Publish_throws_when_message_is_null()
    {
        var sut = new SagaInstance("lorem", "ipsum", "dolor", SagaDescriptor.Create<FakeSaga>());
        Assert.Throws<ArgumentNullException>(() => sut.Publish(null!));
    }

    [Fact]
    public void Publish_should_enqueue_message()
    {
        var message = new DummyMessage();
        var messageEnvelope = DummyMessage.CreateEnvelope();

        var sut = new SagaInstance("lorem", "ipsum", "dolor", SagaDescriptor.Create<FakeSaga>());

        sut.Publish(messageEnvelope);
        Assert.Single(sut.Outbox);
        Assert.Equal(message, sut.Outbox.First().Message);
    }

    [Fact]
    public void ClearOutbox_should_clear_outbox()
    {
        var message = new DummyMessage();
        var messageEnvelope = DummyMessage.CreateEnvelope();
        var sut = new SagaInstance("lorem", "ipsum", "dolor", SagaDescriptor.Create<FakeSaga>());
        sut.Publish(messageEnvelope);
        Assert.Single(sut.Outbox);

        sut.ClearOutbox();
        Assert.Empty(sut.Outbox);
    }
}