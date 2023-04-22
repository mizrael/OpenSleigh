using OpenSleigh.Outbox;
using OpenSleigh.Utils;

namespace OpenSleigh.Transport
{
    internal class DefaultMessageBus : IMessageBus
    {
        private readonly IOutboxRepository _outboxRepository;
        private readonly ISerializer _serializer;
        private readonly ISystemInfo _systemInfo;

        public DefaultMessageBus(IOutboxRepository outboxRepository, ISerializer serializer, ISystemInfo systemInfo)
        {
            _outboxRepository = outboxRepository ?? throw new ArgumentNullException(nameof(outboxRepository));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));
        }

        public async ValueTask PublishAsync<TM>(TM message, CancellationToken cancellationToken = default) 
            where TM : IMessage
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            var outboxMessage = OutboxMessage.Create(message, _systemInfo, _serializer);

           await _outboxRepository.AppendAsync(new [] { outboxMessage }, cancellationToken)
                                .ConfigureAwait(false);
        }
    }
}