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
        if ( hostsCount > _maxHostsCount)
            return;

        var message = new IdempotentMessage("my idempotency key");

        var receivedCount = 0;
        using var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(100) * hostsCount);

        Action<IMessageContext<IdempotentMessage>> onMessage = ctx =>
        {
            Assert.NotNull(ctx.Message);
            Assert.IsType<IdempotentMessage>(ctx.Message);
            Assert.Equal(message.IdempotencyKey, ctx.IdempotencyKey);
            Assert.Equal(message.IdempotencyKey, ctx.Message.IdempotencyKey);

            receivedCount++;
            tokenSource.CancelAfter(TimeSpan.FromSeconds(20));
        };

        await RunScenarioAsync(hostsCount,
            (ctx, services) => services.AddSingleton(onMessage),
            async bus =>
            {
                await bus.PublishAsync(message, tokenSource.Token);
                await bus.PublishAsync(message, tokenSource.Token);
                await bus.PublishAsync(message, tokenSource.Token);
            },
            tokenSource);

        Assert.Equal(1, receivedCount);
    }

    protected override void RegisterSagas(IBusConfigurator cfg)
    {
        cfg.AddSaga<IdempotentSaga>();
    }
}