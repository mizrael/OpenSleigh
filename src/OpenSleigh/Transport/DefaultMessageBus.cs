using Microsoft.Extensions.Logging;
using OpenSleigh.Outbox;
using OpenSleigh.Utils;

namespace OpenSleigh.Transport
{
    internal class DefaultMessageBus : IMessageBus
    {
        private readonly IOutboxRepository _outboxRepository;
        private readonly ISerializer _serializer;
        private readonly ISystemInfo _systemInfo;
        private readonly ITypeResolver _typeResolver;
        private readonly ILogger<DefaultMessageBus> _logger;

        public DefaultMessageBus(
            IOutboxRepository outboxRepository,
            ISerializer serializer,
            ISystemInfo systemInfo,
            ITypeResolver typeResolver,
            ILogger<DefaultMessageBus> logger)
        {
            _outboxRepository = outboxRepository ?? throw new ArgumentNullException(nameof(outboxRepository));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));
            _typeResolver = typeResolver ?? throw new ArgumentNullException(nameof(typeResolver));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async ValueTask<IMessageContext<TM>> PublishAsync<TM>(TM message, CancellationToken cancellationToken = default) 
            where TM : IMessage
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            _typeResolver.Register(message.GetType());

            var outboxMessage = OutboxMessage.Create(message, _systemInfo, _serializer);

            await _outboxRepository.AppendAsync(new [] { outboxMessage }, cancellationToken)
                                   .ConfigureAwait(false);

            _logger.LogInformation("message '{MessageId}' added to outbox.", outboxMessage.MessageId);

            var receipt = MessageContext<TM>.Create(message, outboxMessage);
            return receipt;
        }
    }
}