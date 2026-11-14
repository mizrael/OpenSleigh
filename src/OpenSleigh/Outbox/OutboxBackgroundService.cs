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
        ISystemInfo systemInfo,
        IOutboxSignal? signal = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));
        _signal = signal ?? new OutboxSignal();
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
                // what was signalled so far is about to be read; a message appended during the pass signals again
                _signal.ClearPendingMessages();

                var result = await TryRelayPendingMessagesAsync(stoppingToken).ConfigureAwait(false);

                // a pass that left messages behind (broker down, rows locked) is not followed by another right away,
                // even when it relayed others: the ones that failed would be retried, and logged, at the pace of the drain
                if (result.Relayed > 0 && result.Failed == 0 && _options.DrainPendingMessages)
                    continue;

                // and it waits for the whole interval: being woken by every publish would turn each into a retry
                var canBeWoken = _options.WakeOnPublish && result.Failed == 0;
                await WaitForNextReadAsync(canBeWoken, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async ValueTask<OutboxRelayResult> TryRelayPendingMessagesAsync(CancellationToken stoppingToken)
    {
        try
        {
            return await RelayPendingMessagesAsync(stoppingToken).ConfigureAwait(false);
        }
        // only the cancellation that stops the service ends the relay: any other failure of a pass,
        // a timeout reported as a cancellation included, is retried after the interval
        catch (Exception e) when (e is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            _logger.LogError(
                e,
                "Outbox Background Service could not relay the pending messages on client '{ClientId}', it will try again in {Interval}: {Error}",
                _systemInfo.ClientId,
                _options.Interval,
                e.Message);

            return FailedPass;
        }
    }

    private static readonly OutboxRelayResult FailedPass = new(Relayed: 0, Failed: 1);

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