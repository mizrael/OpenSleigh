using OpenSleigh.Core.Messaging;
using System;

namespace OpenSleigh.Samples.Sample3.Common.Messages
{
    public record StartParentSaga(Guid Id, Guid CorrelationId) : ICommand { }
}
