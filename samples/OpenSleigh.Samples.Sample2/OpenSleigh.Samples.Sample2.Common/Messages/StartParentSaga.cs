using OpenSleigh.Transport;
using System;

namespace OpenSleigh.Samples.Sample2.Common.Messages
{
    public record StartParentSaga(Guid Id, Guid CorrelationId) : IMessage { }
}
