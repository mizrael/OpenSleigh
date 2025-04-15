using Microsoft.AspNetCore.SignalR;
using OpenSleigh.Transport;

namespace OpenSleigh.Samples.Blazor.Hubs;

public class SagaHub : Hub
{
    private readonly IMessageBus _bus;

    public SagaHub(IMessageBus bus)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    }
    
    public async Task StartSaga(string clientId, int stepsCount)
    {
        var message = new Sagas.StartStepsSaga(stepsCount, clientId);

        await _bus.PublishAsync(message);
    }
}
