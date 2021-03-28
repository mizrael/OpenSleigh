using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Samples.Sample4.Common;

namespace OpenSleigh.Samples.Sample4.NotificationsService.EventHandlers
{
    public class ShippingCompletedHandler : IHandleMessage<ShippingCompleted>
    {
        private readonly ILogger<ShippingCompletedHandler> _logger;

        public ShippingCompletedHandler(ILogger<ShippingCompletedHandler> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task HandleAsync(IMessageContext<ShippingCompleted> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"order {context.Message.OrderId} has been shipped.");

            return Task.CompletedTask;
        }
    }
}
