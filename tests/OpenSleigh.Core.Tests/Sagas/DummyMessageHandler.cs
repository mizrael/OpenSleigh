using System;
using System.Threading.Tasks;
using OpenSleigh.Core.Messaging;
using System.Threading;

namespace OpenSleigh.Core.Tests.Sagas
{
    public class DummyMessageHandler : IHandleMessage<DummyMessage>
    {
        public Task HandleAsync(IMessageContext<DummyMessage> context, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}
