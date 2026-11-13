using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Transport;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

public class ParentChildScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    [Theory]
    [MatrixData]
    public async Task run_parent_child_scenario(string combination, int hostsCount)
    {
        var message = new StartParentSaga();
        var completed = new Expectation(1);

        Action<IMessageContext<ParentSagaCompleted>> onMessage = _ => completed.Signal();

        var setup = new HostSetup(bus =>
        {
            bus.AddSaga<ParentSaga>();
            bus.AddSaga<ChildSaga>();
        })
        {
            ConfigureServices = services => services.AddSingleton(onMessage)
        };

        await RunScenarioAsync(combination, hostsCount, setup, async (run, cancellationToken) =>
        {
            await run.AnyHost.PublishAsync(message, cancellationToken);
            await completed.Reached.WaitAsync(cancellationToken);
        });

        Assert.Equal(1, completed.Count);
    }
}
