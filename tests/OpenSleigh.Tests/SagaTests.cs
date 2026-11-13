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

    [Fact]
    public void Publish_with_a_delay_should_add_an_envelope_that_is_due_after_the_delay()
    {
        var instance = new SagaInstance(
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            SagaDescriptor.Create<FakeSaga>());
        var saga = new ConcreteSaga(instance);
        var delay = TimeSpan.FromMinutes(15);

        saga.PublishMessage(new FakeSagaStarter(), delay);

        var envelope = Assert.Single(instance.Outbox);
        Assert.Equal(envelope.CreatedAt + delay, envelope.DeliverAt);
    }

    [Fact]
    public void Publish_with_a_delay_should_throw_when_the_delay_is_negative()
    {
        var instance = new SagaInstance(
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            SagaDescriptor.Create<FakeSaga>());
        var saga = new ConcreteSaga(instance);

        Assert.Throws<ArgumentOutOfRangeException>(() => saga.PublishMessage(new FakeSagaStarter(), TimeSpan.FromSeconds(-1)));
        Assert.Empty(instance.Outbox);
    }

    private class ConcreteSaga : Saga
    {
        public ConcreteSaga(ISagaInstance context) : base(context) { }

        public void PublishMessage<TM>(TM message, TimeSpan delay) where TM : IMessage
        {
            Publish(message, delay);
        }

        public void PublishMessage<TM>(TM message) where TM : IMessage
        {
            Publish(message);
        }
    }
}
