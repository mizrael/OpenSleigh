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
        var received = new Expectation(1);

        Action<IMessageContext<StartSimpleSaga>> onMessage = ctx =>
        {
            received.Require(!string.IsNullOrWhiteSpace(ctx.MessageId), "the message has no id");
            received.Require(!string.IsNullOrWhiteSpace(ctx.SenderId), "the message has no sender id");
            received.Signal();
        };

        await RunScenarioAsync(hostsCount,
            (ctx, services) => services.AddSingleton(onMessage),
            async (services, cancellationToken) =>
            {
                var bus = services.GetRequiredService<IMessageBus>();
                await bus.PublishAsync(message, cancellationToken);
                await received.Reached.WaitAsync(cancellationToken);
            });

        Assert.Empty(received.Problems);
        Assert.Equal(1, received.Count);
    }

    protected override void RegisterSagas(IBusConfigurator cfg)
    {
        cfg.AddSaga<SimpleSaga>();
    }
}
