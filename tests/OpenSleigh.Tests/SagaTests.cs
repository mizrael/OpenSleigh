using OpenSleigh.Outbox;
using OpenSleigh.Transport;

namespace OpenSleigh.Tests;

public class SagaTests
{
    [Fact]
    public void Publish_should_add_envelope_to_context_outbox()
    {
        var descriptor = SagaDescriptor.Create<FakeSaga>();
        var instance = new SagaInstance(
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            descriptor);

        var saga = new ConcreteSaga(instance);

        saga.PublishMessage(new FakeSagaStarter());

        Assert.Single(instance.Outbox);
    }

    [Fact]
    public void Context_should_return_saga_instance()
    {
        var descriptor = SagaDescriptor.Create<FakeSaga>();
        var instance = new SagaInstance(
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            descriptor);

        var saga = new ConcreteSaga(instance);

        Assert.Same(instance, saga.Context);
    }

    [Fact]
    public void Ctor_should_throw_when_context_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => new ConcreteSaga(null!));
    }

    [Fact]
    public void Publish_should_throw_when_message_is_null()
    {
        var descriptor = SagaDescriptor.Create<FakeSaga>();
        var instance = new SagaInstance(
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            descriptor);

        var saga = new ConcreteSaga(instance);

        Assert.Throws<ArgumentNullException>(() => saga.PublishMessage<FakeSagaStarter>(null!));
    }

    private class ConcreteSaga : Saga
    {
        public ConcreteSaga(ISagaInstance context) : base(context) { }

        public void PublishMessage<TM>(TM message) where TM : IMessage
        {
            Publish(message);
        }
    }
}
