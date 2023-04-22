using Microsoft.Extensions.Logging;
using OpenSleigh.Transport;

namespace OpenSleigh
{
    public class SagaRunner : ISagaRunner
    {
        private readonly ISagaExecutionService _sagaExecutionService;
        private readonly IMessageHandlerManager _messageHandlerManager;
        private readonly ILogger<SagaRunner> _logger;

        public SagaRunner(
            ILogger<SagaRunner> logger,
            ISagaExecutionService sagaExecutionService,
            IMessageHandlerManager messageHandlerManager)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sagaExecutionService = sagaExecutionService ?? throw new ArgumentNullException(nameof(sagaExecutionService));
            _messageHandlerManager = messageHandlerManager ?? throw new ArgumentNullException(nameof(messageHandlerManager));
        }

        public async ValueTask ProcessAsync<TM>(
            IMessageContext<TM> messageContext,
            SagaDescriptor descriptor,
            CancellationToken cancellationToken = default)
            where TM : IMessage
        {
            var executionContext = await _sagaExecutionService.StartExecutionContextAsync(messageContext, descriptor, cancellationToken)
                                                               .ConfigureAwait(false);
            
            _logger.LogInformation(
                "Saga '{SagaType}/{InstanceId}' is processing message '{MessageId}'...",
                descriptor.SagaType,
                executionContext.InstanceId,
                messageContext.Id);

            await executionContext.ProcessAsync(_messageHandlerManager, messageContext, _sagaExecutionService, cancellationToken)
                                  .ConfigureAwait(false);

            _logger.LogInformation(
                "Saga '{SagaType}/{InstanceId}' has completed processing message '{MessageId}'.",
                descriptor.SagaType,
                executionContext.InstanceId,
                messageContext.Id);
        }

    }
}