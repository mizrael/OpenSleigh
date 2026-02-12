using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Transport;
using System.ComponentModel;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests;

[Category("E2E")]
[Trait("Category", "E2E")]
public abstract class ParentChildScenario : E2ETestsBase
{
    public ParentChildScenario(ITestOutputHelper console) : base(console) { }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public async Task run_parent_child_scenario(int hostsCount)
    {
        var message = new StartParentSaga();

        var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(100) * hostsCount);

        var contexts = new List<IMessageContext<ParentSagaCompleted>>();

        Action<IMessageContext<ParentSagaCompleted>> onMessage = ctx =>
        {
            contexts.Add(ctx);
            tokenSource.CancelAfter(TimeSpan.FromSeconds(10));
        };

        await RunScenarioAsync(hostsCount,
            (ctx, services) => services.AddSingleton(onMessage),
            async bus => await bus.PublishAsync(message, tokenSource.Token),
            tokenSource);

        Assert.Single(contexts);
    }

    protected override void RegisterSagas(IBusConfigurator cfg)
    {
        cfg.AddSaga<ParentSaga>();
        cfg.AddSaga<ChildSaga>();
    }
}
