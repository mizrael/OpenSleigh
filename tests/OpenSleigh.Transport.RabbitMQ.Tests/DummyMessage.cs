using System;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Transport.RabbitMQ.Tests
{
    public record DummyMessage(Guid Id, Guid CorrelationId) : ICommand
    {
        public static DummyMessage New() => new DummyMessage(Guid.NewGuid(), Guid.NewGuid());
    }
}