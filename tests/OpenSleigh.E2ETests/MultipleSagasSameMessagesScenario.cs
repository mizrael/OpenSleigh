using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.Transport;
using System.ComponentModel;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests;

[Category("E2E")]
[Trait("Category", "E2E")]
public abstract class MultipleSagasSameMessagesScenario : E2ETestsBase
{
    // the start message starts two sagas, and each of them completes once
    private const int SagasCount = 2;

    public MultipleSagasSameMessagesScenario(ITestOutputHelper console) : base(console) { }

    [Theory]
    [HostsCountData]
    public async Task run_multiple_sagas_same_messages_scenario(int hostsCount)
    {
        var message = new StartMultipleSagas();
        var completed = new Expectation(SagasCount);

        Action<IMessageContext<SagaCompleted>> onMessage = _ => completed.Signal();

        await RunScenarioAsync(hostsCount,
            (ctx, services) => services.AddSingleton(onMessage),
            async (services, cancellationToken) =>
            {
                var bus = services.GetRequiredService<IMessageBus>();
                await bus.PublishAsync(message, cancellationToken);
                await completed.Reached.WaitAsync(cancellationToken);
            });

        Assert.Equal(SagasCount, completed.Count);
    }

    protected override void RegisterSagas(IBusConfigurator cfg)
    {
        cfg.AddSaga<SagaWithoutState>();
        cfg.AddSaga<SagaWithState, MySagaState>();
    }
}
