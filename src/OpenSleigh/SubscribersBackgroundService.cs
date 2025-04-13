using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenSleigh.Transport;

namespace OpenSleigh;

public class SubscribersBackgroundService : BackgroundService
{
    private readonly ISystemInfo _systemInfo;
    private readonly ISagaDescriptorsResolver _resolver;
    private readonly IServiceProvider _sp;
    private readonly ILogger<SubscribersBackgroundService> _logger;
    private readonly List<IMessageSubscriber> _subscribers = new();

    public SubscribersBackgroundService(
        ISystemInfo systemInfo,
        ILogger<SubscribersBackgroundService> logger,
        ISagaDescriptorsResolver resolver,
        IServiceProvider serviceProvider)
    {
        _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resolver = resolver;
        _sp = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_systemInfo.PublishOnly)
        {
            _logger.LogInformation($"client '{_systemInfo.ClientId}' is set to Publish Only.");
            return;
        }

        _logger.LogInformation($"starting subscribers on client '{_systemInfo.ClientGroup}/{_systemInfo.ClientId}' ...");

        var subscriberTypeBase = typeof(IMessageSubscriber<>);
        var messageTypes = _resolver.GetRegisteredMessageTypes();
        var subscriberTasks = messageTypes.Select(messageType =>
        {
            var subscriberType = subscriberTypeBase.MakeGenericType(messageType);
            var subscriber = (IMessageSubscriber)_sp.GetRequiredService(subscriberType);
            _subscribers.Add(subscriber);
            return subscriber.StartAsync(stoppingToken).AsTask();
        });
        await Task.WhenAll(subscriberTasks);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_systemInfo.PublishOnly)
        {
            _logger.LogInformation($"stopping subscribers on client '{_systemInfo.ClientId}' ...");
            await Task.WhenAll(_subscribers.Select(s => s.StopAsync(cancellationToken).AsTask()));
        }

        await base.StopAsync(cancellationToken);
    }

}