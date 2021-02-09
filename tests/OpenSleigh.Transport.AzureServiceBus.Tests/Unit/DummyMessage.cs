using System;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Transport.AzureServiceBus.Tests.Unit
{
    public record DummyMessage(Guid Id, Guid CorrelationId) : ICommand
    {
        public static DummyMessage New() => new DummyMessage(Guid.NewGuid(), Guid.NewGuid());
    }
}