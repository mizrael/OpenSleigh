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
        var completed = new Expectation(1);

        Action<IMessageContext<ParentSagaCompleted>> onMessage = _ => completed.Signal();

        await RunScenarioAsync(hostsCount,
            (ctx, services) => services.AddSingleton(onMessage),
            async (services, cancellationToken) =>
            {
                var bus = services.GetRequiredService<IMessageBus>();
                await bus.PublishAsync(message, cancellationToken);
                await completed.Reached.WaitAsync(cancellationToken);
            });

        Assert.Equal(1, completed.Count);
    }

    protected override void RegisterSagas(IBusConfigurator cfg)
    {
        cfg.AddSaga<ParentSaga>();
        cfg.AddSaga<ChildSaga>();
    }
}
