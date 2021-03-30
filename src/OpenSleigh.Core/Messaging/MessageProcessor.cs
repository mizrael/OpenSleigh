using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace OpenSleigh.Core.Messaging
{
    public class MessageProcessor : IMessageProcessor
    {
        private readonly ISagasRunner _sagasRunner;
        private readonly IMessageHandlersRunner _messageHandlersRunner;
        private readonly IMessageContextFactory _messageContextFactory;
        private readonly ILogger<MessageProcessor> _logger;
        
        public MessageProcessor(ISagasRunner sagasRunner, 
            IMessageHandlersRunner messageHandlersRunner, 
            IMessageContextFactory messageContextFactory, 
            ILogger<MessageProcessor> logger)
        {
            _sagasRunner = sagasRunner ?? throw new ArgumentNullException(nameof(sagasRunner));
            _messageHandlersRunner = messageHandlersRunner ?? throw new ArgumentNullException(nameof(messageHandlersRunner));
            _messageContextFactory = messageContextFactory ?? throw new ArgumentNullException(nameof(messageContextFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task ProcessAsync<TM>(TM message, CancellationToken cancellationToken = default)
            where TM : IMessage
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            return ProcessAsyncCore(message, cancellationToken);
        }

        private async Task ProcessAsyncCore<TM>(TM message, CancellationToken cancellationToken) where TM : IMessage
        {
            var messageContext = _messageContextFactory.Create(message);
            
            _logger.LogInformation("processing message {MessageId} with type {MessageType} on client {ClientGroup}/{ClientId} ...",
                                    message.Id, typeof(TM).FullName, messageContext.SystemInfo.ClientGroup, messageContext.SystemInfo.ClientId);

            try
            {
                await _sagasRunner.RunAsync(messageContext, cancellationToken);
                await _messageHandlersRunner.RunAsync(messageContext, cancellationToken);
            }
            catch (AggregateException ex)
            {
                _logger.LogError(ex, "an error has occurred while processing message {MessageId} with type {MessageType} on client {ClientGroup}/{ClientId} : {Exception}",
                    message.Id, typeof(TM).FullName, messageContext.SystemInfo.ClientGroup, messageContext.SystemInfo.ClientId, ex.Message);
                throw;
            }
            
            _logger.LogInformation("message {MessageId} with type {MessageType} processed on client {ClientGroup}/{ClientId}",
                message.Id, typeof(TM).FullName, messageContext.SystemInfo.ClientGroup, messageContext.SystemInfo.ClientId);
        }
    }
}