using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OpenSleigh.Outbox;

public class OutboxBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OutboxProcessorOptions _options;
    private readonly ILogger<OutboxBackgroundService> _logger;
    private readonly ISystemInfo _systemInfo;
    private readonly IOutboxSignal _signal;

    public OutboxBackgroundService(IServiceScopeFactory scopeFactory,
        OutboxProcessorOptions options,
        ILogger<OutboxBackgroundService> logger,
        ISystemInfo systemInfo)
        : this(scopeFactory, options, logger, systemInfo, new OutboxSignal())
    {
    }

    public OutboxBackgroundService(IServiceScopeFactory scopeFactory,
        OutboxProcessorOptions options,
        ILogger<OutboxBackgroundService> logger,
        ISystemInfo systemInfo,
        IOutboxSignal signal)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));
        _signal = signal ?? throw new ArgumentNullException(nameof(signal));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Outbox Background Service is starting on client '{ClientId}' ...",
            _systemInfo.ClientId);

        await ProcessMessagesAsync(stoppingToken);
    }

    private async ValueTask ProcessMessagesAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var result = await RelayPendingMessagesAsync(stoppingToken).ConfigureAwait(false);

                if (result.Relayed > 0 && _options.DrainPendingMessages)
                    continue;

                // a pass that only failed (broker down, rows locked) waits for the whole interval:
                // being woken by every publish would turn each of them into a retry of the backlog
                var canBeWoken = _options.WakeOnPublish && result.Failed == 0;
                await WaitForNextReadAsync(canBeWoken, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async ValueTask<OutboxRelayResult> RelayPendingMessagesAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<IOutboxProcessor>();
        return await service.RelayPendingMessagesAsync(stoppingToken)
            .ConfigureAwait(false);
    }

    private async ValueTask WaitForNextReadAsync(bool canBeWoken, CancellationToken stoppingToken)
    {
        if (canBeWoken)
            await _signal.WaitForPendingMessagesAsync(_options.Interval, stoppingToken)
                .ConfigureAwait(false);
        else
            await Task.Delay(_options.Interval, stoppingToken)
                .ConfigureAwait(false);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Outbox Background Service is stopping on client '{ClientId}'.", _systemInfo.ClientId);

        await base.StopAsync(cancellationToken);
    }
}