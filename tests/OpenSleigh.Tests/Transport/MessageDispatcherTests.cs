using OpenSleigh.Outbox;
using OpenSleigh.Transport;

namespace OpenSleigh.Tests.Transport;

public class MessageDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_should_create_context_and_call_runner()
    {
        var message = new FakeSagaStarter();
        var sagaInstance = Substitute.For<ISagaInstance>();
        sagaInstance.CorrelationId.Returns(Guid.NewGuid().ToString());
        sagaInstance.InstanceId.Returns(Guid.NewGuid().ToString());
        var envelope = MessageEnvelope.Create(message, sagaInstance);
        var descriptor = SagaDescriptor.Create<FakeSaga>();
        var runner = Substitute.For<ISagaRunner>();

        var sut = new MessageDispatcher<FakeSagaStarter>();

        await sut.DispatchAsync(envelope, runner, new[] { descriptor }, CancellationToken.None);

        await runner.Received(1).ProcessAsync(
            Arg.Is<IMessageContext<FakeSagaStarter>>(ctx =>
                ctx.MessageId == envelope.MessageId &&
                ctx.CorrelationId == envelope.CorrelationId),
            descriptor,
            Arg.Any<CancellationToken>());
    }
}
