using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Samples.Sample4.Common;

namespace OpenSleigh.Samples.Sample4.API.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class OrdersController : ControllerBase
    {
        private readonly IMessageBus _bus;

        public OrdersController(IMessageBus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        }

        [HttpPost]
        public async Task<IActionResult> PostOrder(CancellationToken cancellationToken = default)
        {
            var message = SaveOrder.New(Guid.NewGuid());
            
            await _bus.PublishAsync(message, cancellationToken);

            return Accepted(new
            {
                SagaId = message.CorrelationId
            });
        }
    }
}
