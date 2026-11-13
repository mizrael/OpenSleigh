using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Transport;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

public class MultipleSagasSameMessagesScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    // the start message starts two sagas, and each of them completes once
    private const int SagasCount = 2;

    // the two sagas are handled one after the other, and the first is stored and publishes before the second exists.
    // With several hosts, its ProcessSaga, and then its SagaCompleted, can reach another host before the second
    // saga is stored: by design that message is dead-lettered for the second saga, after the first has handled it.
    // Both sagas complete all the same, the second through the messages it publishes itself
    private static readonly Type[] CanArriveBeforeTheSecondSaga = [typeof(ProcessSaga), typeof(SagaCompleted)];

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

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                await run.AnyHost.PublishAsync(message, cancellationToken);
                await completed.Reached.WaitAsync(cancellationToken);
            },
            async run =>
            {
                await AssertOnlySagaNotFoundDeadLettersAsync(run, CanArriveBeforeTheSecondSaga);

                // each of the two messages exists once before the second saga does
                var arrivedEarly = run.ProcessingFailures.Where(failure => failure.IsSagaNotFound).ToArray();
                Assert.Equal(arrivedEarly.Length, arrivedEarly.Select(failure => failure.MessageType).Distinct().Count());

                // a host on its own handles one message at a time: nothing overtakes the second saga
                if (hostsCount == 1)
                    Assert.Empty(arrivedEarly);
            });

        Assert.Equal(SagasCount, completed.Count);
    }
}
