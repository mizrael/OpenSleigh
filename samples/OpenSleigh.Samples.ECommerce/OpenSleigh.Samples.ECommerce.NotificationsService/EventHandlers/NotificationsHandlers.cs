using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenSleigh.Samples.ECommerce.Common;
using OpenSleigh.Transport;

namespace OpenSleigh.Samples.ECommerce.NotificationsService.EventHandlers;

public class NotificationsHandlers : 
    Saga,
    IStartedBy<ShippingCompleted>,
    IStartedBy<OrderSagaCompleted>
{
    private readonly ILogger<NotificationsHandlers> _logger;

    public NotificationsHandlers(
        ILogger<NotificationsHandlers> logger,
        ISagaExecutionContext context) : base(context)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ValueTask HandleAsync(IMessageContext<ShippingCompleted> context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"order {context.Message.OrderId} has been shipped.");

        return ValueTask.CompletedTask;
    }
    
    public ValueTask HandleAsync(IMessageContext<OrderSagaCompleted> context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"order {context.Message.OrderId} processed successfully!");

        return ValueTask.CompletedTask;
    }
}
