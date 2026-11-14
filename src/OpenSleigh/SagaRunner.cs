using Microsoft.Extensions.Logging;
using OpenSleigh.Transport;

namespace OpenSleigh;

public class SagaRunner : ISagaRunner
{
    private readonly ISagaExecutionService _sagaExecutionService;
    private readonly IMessageHandlerManager _messageHandlerManager;
    private readonly ILogger<SagaRunner> _logger;

    private readonly SagaInstanceGate? _instanceGate;

    public SagaRunner(
        ILogger<SagaRunner> logger,
        ISagaExecutionService sagaExecutionService,
        IMessageHandlerManager messageHandlerManager,
        SagaInstanceGate? instanceGate = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sagaExecutionService = sagaExecutionService ?? throw new ArgumentNullException(nameof(sagaExecutionService));
        _messageHandlerManager = messageHandlerManager ?? throw new ArgumentNullException(nameof(messageHandlerManager));
        _instanceGate = instanceGate;
    }

    // SPIKE: one message at a time per saga instance in this process, and a message whose append was refused
    // is handled again here before it is given back to the transport
    public async ValueTask ProcessAsync<TM>(
        IMessageContext<TM> messageContext,
        SagaDescriptor descriptor,
        CancellationToken cancellationToken = default)
        where TM : IMessage
    {
        if (_instanceGate is null)
        {
            await ProcessOnceAsync(messageContext, descriptor, cancellationToken).ConfigureAwait(false);
            return;
        }

        var key = $"{descriptor.SagaType.FullName}|{messageContext.CorrelationId}";
        using var turn = await _instanceGate.EnterAsync(key, cancellationToken).ConfigureAwait(false);

        for (var retry = 0; ; retry++)
        {
            try
            {
                await ProcessOnceAsync(messageContext, descriptor, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception conflict) when (LedgerSpike.Mode == LedgerConcurrencyMode.NoLock && conflict.IsLockConflict())
            {
                if (retry >= LedgerSpike.ConflictRetries)
                {
                    LedgerSpike.CountConflictLeftToTransport();
                    throw;
                }

                LedgerSpike.CountInProcessRetry();
            }
        }
    }

    private async ValueTask ProcessOnceAsync<TM>(
        IMessageContext<TM> messageContext,
        SagaDescriptor descriptor,
        CancellationToken cancellationToken)
        where TM : IMessage
    {
        var sagaInstance = await _sagaExecutionService.BeginProcessingAsync(messageContext, descriptor, cancellationToken)
                                                      .ConfigureAwait(false);

        _logger.LogInformation(
            "Saga '{SagaType}/{InstanceId}' is processing message '{MessageId}'...",
            descriptor.SagaType,
            sagaInstance.InstanceId,
            messageContext.MessageId);

        await sagaInstance.ProcessAsync(_messageHandlerManager, messageContext, _sagaExecutionService, cancellationToken)
                          .ConfigureAwait(false);

        _logger.LogInformation(
            "Saga '{SagaType}/{InstanceId}' has completed processing message '{MessageId}'.",
            descriptor.SagaType,
            sagaInstance.InstanceId,
            messageContext.MessageId);
    }
}