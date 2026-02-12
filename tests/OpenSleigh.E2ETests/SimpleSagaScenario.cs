using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Transport;
using System.ComponentModel;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests;

[Category("E2E")]
[Trait("Category", "E2E")]
public abstract class SimpleSagaScenario : E2ETestsBase
{
    public SimpleSagaScenario(ITestOutputHelper console) : base(console) { }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public async Task run_single_message_scenario(int hostsCount)
    {
        var message = new StartSimpleSaga();

        var receivedCount = 0;
        using var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10) * hostsCount);

        Action<IMessageContext<StartSimpleSaga>> onMessage = ctx =>
        {
            Assert.False(string.IsNullOrWhiteSpace(ctx.MessageId));
            Assert.False(string.IsNullOrWhiteSpace(ctx.SenderId));

            Interlocked.Increment(ref receivedCount);
            tokenSource.CancelAfter(TimeSpan.FromSeconds(5));
        };

        await RunScenarioAsync(hostsCount,
            (ctx, services) => services.AddSingleton(onMessage),
            async bus => await bus.PublishAsync(message, tokenSource.Token),
            tokenSource);

        await Task.Delay(500);

        Assert.Equal(1, receivedCount);
    }

    protected override void RegisterSagas(IBusConfigurator cfg)
    {
        cfg.AddSaga<SimpleSaga>();
    }
}
