using FluentAssertions;
using OpenSleigh.Transport;

namespace OpenSleigh.Tests;

public class SagaInstanceFactoryTests
{
    [Fact]
    public void Create_should_throw_when_descriptor_is_null()
    {
        var sut = new SagaInstanceFactory();
        var messageContext = FakeMessageContext<FakeSagaStarter>.Create(new FakeSagaStarter());

        var ex = Assert.Throws<ArgumentNullException>(() => sut.Create(null!, messageContext));
        ex.ParamName.Should().Be("descriptor");
    }

    [Fact]
    public void Create_should_throw_when_messageContext_is_null()
    {
        var sut = new SagaInstanceFactory();
        var descriptor = SagaDescriptor.Create<FakeSaga>();

        var ex = Assert.Throws<ArgumentNullException>(() => sut.Create<FakeSagaStarter>(descriptor, null!));
        ex.ParamName.Should().Be("messageContext");
    }

    [Fact]
    public void Create_should_return_SagaInstance_when_no_state_type()
    {
        var sut = new SagaInstanceFactory();
        var descriptor = SagaDescriptor.Create<FakeSaga>();
        var messageContext = FakeMessageContext<FakeSagaStarter>.Create(new FakeSagaStarter());

        var result = sut.Create(descriptor, messageContext);

        result.Should().NotBeNull();
        result.Should().BeOfType<SagaInstance>();
        result.CorrelationId.Should().Be(messageContext.CorrelationId);
        result.TriggerMessageId.Should().Be(messageContext.MessageId);
        result.Descriptor.Should().Be(descriptor);
    }

    [Fact]
    public void Create_should_return_SagaInstance_with_state_when_state_type_provided()
    {
        var sut = new SagaInstanceFactory();
        var descriptor = SagaDescriptor.Create<FakeSagaWithState, int>();
        var messageContext = FakeMessageContext<FakeSagaStarter>.Create(new FakeSagaStarter());

        var result = sut.Create(descriptor, messageContext);

        result.Should().NotBeNull();
        result.Should().BeOfType<SagaInstance<int>>();
        result.CorrelationId.Should().Be(messageContext.CorrelationId);
        result.TriggerMessageId.Should().Be(messageContext.MessageId);
        result.Descriptor.Should().Be(descriptor);
        
        var typedResult = (SagaInstance<int>)result;
        typedResult.State.Should().Be(0); // default int value
    }

    [Fact]
    public void Create_should_return_SagaInstance_with_custom_class_state()
    {
        var sut = new SagaInstanceFactory();
        var descriptor = SagaDescriptor.Create<TestSagaWithCustomState, TestState>();
        var messageContext = FakeMessageContext<FakeSagaStarter>.Create(new FakeSagaStarter());

        var result = sut.Create(descriptor, messageContext);

        result.Should().NotBeNull();
        result.Should().BeOfType<SagaInstance<TestState>>();
        
        var typedResult = (SagaInstance<TestState>)result;
        typedResult.State.Should().NotBeNull();
    }
}

// Test helper classes
internal class TestState
{
    public string Name { get; set; } = "default";
}

internal class TestSagaWithCustomState : 
    Saga<TestState>, 
    IStartedBy<FakeSagaStarter>
{
    public TestSagaWithCustomState(ISagaInstance<TestState> context) : base(context)
    {
    }

    public ValueTask HandleAsync(IMessageContext<FakeSagaStarter> messageContext, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
