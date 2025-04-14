using OpenSleigh.Outbox;
using OpenSleigh.Transport;

namespace OpenSleigh.Tests;

public record DummyMessage : IMessage
{
    public static OutboxMessage CreateOutboxMessage(string? parentId = null)
    {
        var body = new byte[] { 1, 2, 3 };
        OutboxMessage.TryCreate(
            body, 
            "message id", 
            "correlation id", 
            DateTimeOffset.UtcNow, 
            typeof(DummyMessage),
            parentId, 
            "sender", out var message);
        return message;
    }
}
