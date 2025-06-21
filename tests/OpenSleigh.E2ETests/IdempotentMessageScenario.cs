using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.E2ETests.Sagas;
using OpenSleigh.Transport;
using System.ComponentModel;

namespace OpenSleigh.E2ETests;

[Category("E2E")]
[Trait("Category", "E2E")]
public abstract class IdempotentMessageScenario : E2ETestsBase
{
    private int _maxHostsCount = 10;

    protected IdempotentMessageScenario(int maxHostsCount = 10)
    {
        _maxHostsCount = maxHostsCount;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public async Task run_idempotent_message_scenario(int hostsCount)
    {
        if (hostsCount > _maxHostsCount)
            return;

        var requestId = Guid.CreateVersion7().ToString("N");
        var correlationId = Guid.CreateVersion7().ToString("N");
        var message = new IdempotentMessage(requestId, correlationId, hostsCount);

        var receivedCount = 0;
        using var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5) * hostsCount);

        Action<IMessageContext<IdempotentMessage>> onMessage = ctx =>
        {
            Assert.NotNull(ctx.Message);
            Assert.Equal(message.CorrelationId, ctx.CorrelationId);
            Assert.Equal(message.CorrelationId, ctx.Message.CorrelationId);
            Assert.Equal(requestId, ctx.Message.RequestId);

            receivedCount++;
            tokenSource.CancelAfter(TimeSpan.FromSeconds(2));
        };

        await RunScenarioAsync(hostsCount,
            (ctx, services) => services.AddSingleton(onMessage),
            async bus =>
            {
                await PublishAsync(bus, message, tokenSource.Token);
                await PublishAsync(bus, message, tokenSource.Token);
                await PublishAsync(bus, message, tokenSource.Token);
                await PublishAsync(bus, message, tokenSource.Token);
                await PublishAsync(bus, message, tokenSource.Token);
            },
            tokenSource);

        Assert.Equal(1, receivedCount);
    }

    private async ValueTask PublishAsync(
        IMessageBus bus,
        IdempotentMessage message,
        CancellationToken cancellationToken)
    {
        var result = Outbox.OutboxAppendResult.Undefined; 
        while(result != Outbox.OutboxAppendResult.Success)
        {
            try
            {
                await Task.Delay(100, cancellationToken);
                result = await bus.PublishAsync(message, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    protected override void RegisterSagas(IBusConfigurator cfg)
    {
        cfg.AddSaga<IdempotentSaga>();
    }
}