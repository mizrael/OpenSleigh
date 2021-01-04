using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Core
{
    public class MessageProcessor : IMessageProcessor
    {
        private readonly ISagasRunner _sagasRunner;
        private readonly IMessageContextFactory _messageContextFactory;

        public MessageProcessor(ISagasRunner sagasRunner, IMessageContextFactory messageContextFactory)
        {
            _sagasRunner = sagasRunner ?? throw new ArgumentNullException(nameof(sagasRunner));
            _messageContextFactory = messageContextFactory ?? throw new ArgumentNullException(nameof(messageContextFactory));
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

            await _sagasRunner.RunAsync(messageContext, cancellationToken);
        }
    }
}