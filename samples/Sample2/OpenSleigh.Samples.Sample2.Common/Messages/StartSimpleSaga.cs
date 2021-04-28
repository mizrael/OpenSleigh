using System;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Samples.Sample2.Common.Messages
{
    public record StartSimpleSaga(Guid Id, Guid CorrelationId) : ICommand { }   
}
