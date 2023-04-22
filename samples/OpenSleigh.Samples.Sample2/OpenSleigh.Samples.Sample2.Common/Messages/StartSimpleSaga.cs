using System;
using OpenSleigh.Transport;

namespace OpenSleigh.Samples.Sample2.Common.Messages
{
    public record StartSimpleSaga(Guid Id, Guid CorrelationId) : IMessage { }   
}
