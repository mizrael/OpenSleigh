using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenSleigh.Transport;

namespace OpenSleigh;

public class SubscribersBackgroundService : BackgroundService
{
    private readonly ISystemInfo _systemInfo;
    private readonly ILogger<SubscribersBackgroundService> _logger;
    private readonly IEnumerable<IMessageSubscriber> _subscribers;

    public SubscribersBackgroundService(
        ISystemInfo systemInfo,
        ILogger<SubscribersBackgroundService> logger,
        IEnumerable<IMessageSubscriber> subscribers)
    {
        _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _subscribers = subscribers ?? Array.Empty<IMessageSubscriber>();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_systemInfo.PublishOnly)
        {
            _logger.LogInformation($"client '{_systemInfo.ClientId}' is set to Publish Only.");
            return;
        }

        _logger.LogInformation($"starting subscribers on client '{_systemInfo.ClientGroup}/{_systemInfo.ClientId}' ...");

        var subscriberTasks = _subscribers.Select(subscriber =>
        {
            return subscriber.StartAsync(stoppingToken).AsTask();
        }).ToArray();
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