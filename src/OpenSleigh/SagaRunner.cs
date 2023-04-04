using Microsoft.Extensions.Logging;
using OpenSleigh.Transport;

namespace OpenSleigh
{
    public class SagaRunner : ISagaRunner
    {
        private readonly ISagaExecutionService _sagaExecutionService;
        private readonly IMessageHandlerManager _messageHandlerRunner;
        private readonly ILogger<SagaRunner> _logger;

        public SagaRunner(
            ILogger<SagaRunner> logger,
            ISagaExecutionService sagaExecutionService,
            IMessageHandlerManager messageHandlerRunner)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sagaExecutionService = sagaExecutionService ?? throw new ArgumentNullException(nameof(sagaExecutionService));
            _messageHandlerRunner = messageHandlerRunner ?? throw new ArgumentNullException(nameof(messageHandlerRunner));
        }

        public async ValueTask ProcessAsync<TM>(
            IMessageContext<TM> messageContext,
            SagaDescriptor descriptor,
            CancellationToken cancellationToken = default)
            where TM : IMessage
        {
            var (executionContext, lockId) = await _sagaExecutionService.BeginAsync(messageContext, descriptor, cancellationToken)
                                                                        .ConfigureAwait(false);
            if (executionContext is null)
            {
                // TODO: log
                return;
            }

            _logger.LogInformation(
                "Saga '{SagaType}/{InstanceId}' is processing message '{MessageId}'...",
                descriptor.SagaType,
                executionContext.InstanceId,
                messageContext.Id);

            await _messageHandlerRunner.ProcessAsync(messageContext, executionContext, cancellationToken)
                                        .ConfigureAwait(false);

            await _sagaExecutionService.CommitAsync(executionContext, messageContext, lockId, cancellationToken)
                                        .ConfigureAwait(false);

            _logger.LogInformation(
                "Saga '{SagaType}/{InstanceId}' has completed processing message '{MessageId}'.",
                descriptor.SagaType,
                executionContext.InstanceId,
                messageContext.Id);
        }

    }
}