using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Transport;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

public class MultipleSagasSameMessagesScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    // the start message starts two sagas, and each of them completes once
    private const int SagasCount = 2;

    [Theory]
    [MatrixData]
    public async Task run_multiple_sagas_same_messages_scenario(string combination, int hostsCount)
    {
        var message = new StartMultipleSagas();
        var completed = new Expectation(SagasCount);

        Action<IMessageContext<SagaCompleted>> onMessage = _ => completed.Signal();

        var setup = new HostSetup(bus =>
        {
            bus.AddSaga<SagaWithoutState>();
            bus.AddSaga<SagaWithState, MySagaState>();
        })
        {
            ConfigureServices = services => services.AddSingleton(onMessage)
        };

        await RunScenarioAsync(combination, hostsCount, setup, async (run, cancellationToken) =>
        {
            await run.AnyHost.PublishAsync(message, cancellationToken);
            await completed.Reached.WaitAsync(cancellationToken);
        });

        Assert.Equal(SagasCount, completed.Count);
    }
}
