using OpenSleigh.Outbox;
using System;

namespace OpenSleigh.Transport.Kafka.Tests.Unit
{
    public record DummyMessage : IMessage
    {
        public static OutboxMessage CreateOutboxMessage()
        {
            var body = new byte[] { 1, 2, 3 };
            OutboxMessage.TryCreate(body, "message id", "correlation id", DateTimeOffset.UtcNow, typeof(DummyMessage), null, "sender", out var message);
            return message;
        }
    }
}
