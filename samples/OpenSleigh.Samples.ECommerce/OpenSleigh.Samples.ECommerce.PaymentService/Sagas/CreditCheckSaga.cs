using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenSleigh.Samples.ECommerce.Common;
using OpenSleigh.Transport;

namespace OpenSleigh.Samples.ECommerce.PaymentService.Sagas;

public record CreditCheckSagaState;

public class CreditCheckSaga :
    Saga<CreditCheckSagaState>,
    IStartedBy<ProcessCreditCheck>
{
    private readonly ILogger<CreditCheckSaga> _logger;

    public CreditCheckSaga(
        ISagaExecutionContext<CreditCheckSagaState> context,
        ILogger<CreditCheckSaga> logger) : base(context)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    public async ValueTask HandleAsync(IMessageContext<ProcessCreditCheck> context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"processing credit check for order '{context.Message.OrderId}'...");
        
        var message = new CrediCheckCompleted(context.Message.OrderId);
        this.Publish(message);

        _logger.LogInformation($"credit check for order '{context.Message.OrderId}' processed!");

        this.Context.MarkAsCompleted();
    }
}
