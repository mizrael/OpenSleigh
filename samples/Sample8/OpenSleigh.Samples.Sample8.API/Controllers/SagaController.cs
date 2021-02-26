using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Samples.Sample8.API.Sagas;

namespace OpenSleigh.Samples.Sample8.API.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class SagaController : ControllerBase
    {
        private readonly IMessageBus _bus;

        public SagaController(IMessageBus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        }

        [HttpPost]
        public async Task<IActionResult> StartSimpleSaga(int stepsCount, CancellationToken cancellationToken = default)
        {
            var message = new StartSaga(stepsCount, Guid.NewGuid(), Guid.NewGuid());
            
            await _bus.PublishAsync(message, cancellationToken);

            return Accepted(new
            {
                SagaId = message.CorrelationId
            });
        }
    }
}
