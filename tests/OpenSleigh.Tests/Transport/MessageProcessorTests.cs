using OpenSleigh.Outbox;
using OpenSleigh.Transport;
using OpenSleigh.Utils;

namespace OpenSleigh.Tests.Transport;

public class MessageProcessorTests
{
    [Fact]
    public async Task ProcessAsync_should_throw_when_outboxMessage_is_null()
    {
        var sagaRunner = Substitute.For<ISagaRunner>();
        var resolver = Substitute.For<ISagaDescriptorsResolver>();
        var serializer = Substitute.For<ISerializer>();

        var sut = new MessageProcessor(sagaRunner, resolver, serializer);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => sut.ProcessAsync(null!, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task ProcessAsync_should_resolve_descriptors_and_dispatch()
    {
        var sagaRunner = Substitute.For<ISagaRunner>();
        var resolver = Substitute.For<ISagaDescriptorsResolver>();
        var serializer = Substitute.For<ISerializer>();

        var envelope = DummyMessage.CreateEnvelope();
        var descriptor = SagaDescriptor.Create<FakeSaga>();

        resolver.Resolve(envelope.Message).Returns(new[] { descriptor });

        var sut = new MessageProcessor(sagaRunner, resolver, serializer);

        await sut.ProcessAsync(envelope, CancellationToken.None);

        await sagaRunner.Received(1).ProcessAsync(
            Arg.Any<IMessageContext<DummyMessage>>(),
            descriptor,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Ctor_should_throw_when_sagaRunner_is_null()
    {
        var resolver = Substitute.For<ISagaDescriptorsResolver>();
        var serializer = Substitute.For<ISerializer>();
        Assert.Throws<ArgumentNullException>(() => new MessageProcessor(null!, resolver, serializer));
    }

    [Fact]
    public void Ctor_should_throw_when_resolver_is_null()
    {
        var sagaRunner = Substitute.For<ISagaRunner>();
        var serializer = Substitute.For<ISerializer>();
        Assert.Throws<ArgumentNullException>(() => new MessageProcessor(sagaRunner, null!, serializer));
    }
}
