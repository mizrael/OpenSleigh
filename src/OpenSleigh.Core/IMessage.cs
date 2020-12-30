using System;

namespace OpenSleigh.Core
{
    public interface IMessage
    {
        Guid Id { get; }
        Guid CorrelationId { get; }
    }
}