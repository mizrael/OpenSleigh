using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Samples.Sample10.Common;

namespace OpenSleigh.Samples.Sample10.NotificationsService.EventHandlers
{
    public class NotificationsHandler : 
        IHandleMessage<ShippingCompleted>,
        IHandleMessage<OrderSagaCompleted>
    {
        private readonly ILogger<NotificationsHandler> _logger;

        public NotificationsHandler(ILogger<NotificationsHandler> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task HandleAsync(IMessageContext<ShippingCompleted> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"order {context.Message.OrderId} has been shipped.");

            return Task.CompletedTask;
        }
        
        public Task HandleAsync(IMessageContext<OrderSagaCompleted> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"order {context.Message.OrderId} processed successfully!");

            return Task.CompletedTask;
        }
    }
}
