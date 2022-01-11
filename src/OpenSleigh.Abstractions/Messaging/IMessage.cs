using System;

namespace OpenSleigh.Core.Messaging
{
    public interface IMessage
    {
        Guid Id { get; }
        Guid CorrelationId { get; }
    }
}