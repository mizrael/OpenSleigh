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
}
