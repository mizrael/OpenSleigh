using OpenSleigh.Outbox;
using OpenSleigh.Transport;

namespace OpenSleigh.Tests;

public record DummyMessage : IMessage
{
    public static MessageEnvelope CreateEnvelope(string? parentId = null)
    {
        var message = new DummyMessage();

        var context = NSubstitute.Substitute.For<ISagaInstance >();
        context.CorrelationId.Returns(Guid.NewGuid().ToString());
        context.TriggerMessageId.Returns(parentId);
        context.InstanceId.Returns(Guid.NewGuid().ToString());

        return MessageEnvelope.Create(message, context);
    }
}
