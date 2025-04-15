using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenSleigh.Samples.ECommerce.Common;
using OpenSleigh.Transport;

namespace OpenSleigh.Samples.ECommerce.ShippingService.Sagas;

public record ShippingSagaState;

public class ShippingSaga :
    Saga<ShippingSagaState>,
    IStartedBy<ProcessShipping>
{
    private readonly ILogger<ShippingSaga> _logger;

    public ShippingSaga(
        ISagaExecutionContext<ShippingSagaState> context,
        ILogger<ShippingSaga> logger) : base(context)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    public async ValueTask HandleAsync(IMessageContext<ProcessShipping> context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"processing shipping for order '{context.Message.OrderId}'...");
        
        var message = new ShippingCompleted(context.Message.OrderId);
        this.Publish(message);

        _logger.LogInformation($"order '{context.Message.OrderId}' shipped!");

        this.Context.MarkAsCompleted();
    }
}
