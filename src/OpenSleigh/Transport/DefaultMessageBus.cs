using Microsoft.Extensions.Logging;
using OpenSleigh.Outbox;
using OpenSleigh.Utils;

namespace OpenSleigh.Transport;

internal class DefaultMessageBus : IMessageBus
{
    private readonly IOutboxRepository _outboxRepository;
    private readonly ISystemInfo _systemInfo;
    private readonly ITypeResolver _typeResolver;
    private readonly ILogger<DefaultMessageBus> _logger;

    public DefaultMessageBus(
        IOutboxRepository outboxRepository,
        ISystemInfo systemInfo,
        ITypeResolver typeResolver,
        ILogger<DefaultMessageBus> logger)
    {
        _outboxRepository = outboxRepository ?? throw new ArgumentNullException(nameof(outboxRepository));
        _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));
        _typeResolver = typeResolver ?? throw new ArgumentNullException(nameof(typeResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask PublishAsync<TM>(TM message, CancellationToken cancellationToken = default) 
        where TM : IMessage
    {
        ArgumentNullException.ThrowIfNull(message, nameof(message));

        _logger.LogInformation("appending message to outbox...");

        _typeResolver.Register(message.GetType());

        var outboxMessage = MessageEnvelope.Create(message, _systemInfo);

        await _outboxRepository.AppendAsync([outboxMessage], cancellationToken)
                               .ConfigureAwait(false);

        _logger.LogInformation("message '{MessageId}' added to outbox.", outboxMessage.MessageId);
    }
}