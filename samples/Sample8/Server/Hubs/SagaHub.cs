using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Samples.Sample8.Server.Sagas;

namespace OpenSleigh.Samples.Sample8.Server.Hubs
{
    public class SagaHub : Hub
    {
        private readonly IMessageBus _bus;

        public SagaHub(IMessageBus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        }
        
        public async Task StartSaga(string clientId, int stepsCount)
        {
            var message = new StartSaga(stepsCount, clientId, Guid.NewGuid(), Guid.NewGuid());

            await _bus.PublishAsync(message);
        }
    }
}
