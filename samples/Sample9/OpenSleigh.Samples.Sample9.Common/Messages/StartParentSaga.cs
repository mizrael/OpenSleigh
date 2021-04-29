using System;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Samples.Sample9.Common.Messages
{
    public record StartParentSaga(Guid Id, Guid CorrelationId) : ICommand { }
}
