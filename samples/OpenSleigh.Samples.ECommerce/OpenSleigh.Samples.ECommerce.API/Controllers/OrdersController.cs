using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using OpenSleigh.Samples.ECommerce.Common;
using OpenSleigh.Transport;

namespace OpenSleigh.Samples.ECommerce.API.Controllers;

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
#if NET9_0_OR_GREATER
        var message = new SaveOrder(OrderId: Guid.CreateVersion7());
#else
        var message = new SaveOrder(OrderId: Guid.NewGuid());
#endif
        
        await _bus.PublishAsync(message, cancellationToken);

        return Accepted(new
        {
            OrderId = message.OrderId
        });
    }
}
