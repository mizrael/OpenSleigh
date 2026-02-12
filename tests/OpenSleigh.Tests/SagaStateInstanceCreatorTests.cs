namespace OpenSleigh.Tests;

public class SagaStateInstanceCreatorTests
{
    [Fact]
    public void Create_should_return_typed_saga_instance()
    {
        var descriptor = SagaDescriptor.Create<FakeSagaWithState, int>();
        var state = 42;

        var sut = new SagaStateInstanceCreator<int>();
        var result = sut.Create(state, "trigger-1", "corr-1", descriptor);

        Assert.NotNull(result);
        var typed = Assert.IsType<SagaInstance<int>>(result);
        Assert.Equal(42, typed.State);
        Assert.Equal("trigger-1", typed.TriggerMessageId);
        Assert.Equal("corr-1", typed.CorrelationId);
        Assert.Equal(descriptor, typed.Descriptor);
        Assert.NotEmpty(typed.InstanceId);
    }

    [Fact]
    public void Create_should_cast_state_from_object()
    {
        var descriptor = SagaDescriptor.Create<FakeSagaWithState, int>();
        object state = 99;

        var sut = new SagaStateInstanceCreator<int>();
        var result = sut.Create(state, "trigger-2", "corr-2", descriptor);

        var typed = Assert.IsType<SagaInstance<int>>(result);
        Assert.Equal(99, typed.State);
    }

    [Fact]
    public void Create_should_throw_on_invalid_cast()
    {
        var descriptor = SagaDescriptor.Create<FakeSagaWithState, int>();
        object state = "not an int";

        var sut = new SagaStateInstanceCreator<int>();

        Assert.Throws<InvalidCastException>(() =>
            sut.Create(state, "trigger-3", "corr-3", descriptor));
    }
}
