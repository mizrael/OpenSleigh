using System;
using System.Threading;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using OpenSleigh.Samples.Sample2.Common.Messages;
using OpenSleigh.Transport;

namespace OpenSleigh.Samples.Sample2.API.Controllers
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
        public async Task<IActionResult> StartSimpleSaga(bool isSimple = true, CancellationToken cancellationToken = default)
        {
            IMessage message = isSimple ? new StartSimpleSaga(Guid.NewGuid(), Guid.NewGuid()) :
                new StartParentSaga(Guid.NewGuid(), Guid.NewGuid());
            
            var receipt = await _bus.PublishAsync(message, cancellationToken);

            return Accepted(new
            {
                SagaId = receipt.CorrelationId
            });
        }
    }
}
