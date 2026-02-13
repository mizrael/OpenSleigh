using OpenSleigh.Transport;

namespace OpenSleigh.Tests;

public class SagaInstanceFactoryTests
{
    [Fact]
    public void Create_should_throw_when_descriptor_is_null()
    {
        var sut = new SagaInstanceFactory();
        var messageContext = FakeMessageContext<FakeSagaStarter>.Create(new FakeSagaStarter());

        Assert.Throws<ArgumentNullException>(() => sut.Create(null!, messageContext));
    }

    [Fact]
    public void Create_should_throw_when_messageContext_is_null()
    {
        var sut = new SagaInstanceFactory();
        var descriptor = SagaDescriptor.Create<FakeSaga>();

        Assert.Throws<ArgumentNullException>(() => sut.Create<FakeSagaStarter>(descriptor, null!));
    }

    [Fact]
    public void Create_should_return_SagaInstance_without_state_when_SagaStateType_is_null()
    {
        var sut = new SagaInstanceFactory();
        var descriptor = SagaDescriptor.Create<FakeSaga>();
        var messageContext = FakeMessageContext<FakeSagaStarter>.Create(new FakeSagaStarter());

        var result = sut.Create(descriptor, messageContext);

        Assert.NotNull(result);
        Assert.IsType<SagaInstance>(result);
        Assert.Equal(messageContext.MessageId, result.TriggerMessageId);
        Assert.Equal(messageContext.CorrelationId, result.CorrelationId);
    }

    [Fact]
    public void Create_should_return_SagaInstance_with_state_when_SagaStateType_is_set()
    {
        var sut = new SagaInstanceFactory();
        var descriptor = SagaDescriptor.Create<FakeSagaWithState, int>();
        var messageContext = FakeMessageContext<FakeSagaStarter>.Create(new FakeSagaStarter());

        var result = sut.Create(descriptor, messageContext);

        Assert.NotNull(result);
        Assert.IsAssignableFrom<ISagaInstance<int>>(result);
        Assert.Equal(messageContext.MessageId, result.TriggerMessageId);
        Assert.Equal(messageContext.CorrelationId, result.CorrelationId);
    }
}
