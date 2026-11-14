using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Transport;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

public class SimpleSagaScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    [Theory]
    [MatrixData]
    public async Task run_single_message_scenario(string combination, int hostsCount)
    {
        var message = new StartSimpleSaga();
        var received = new Expectation(1);

        Action<IMessageContext<StartSimpleSaga>> onMessage = ctx =>
        {
            received.Require(!string.IsNullOrWhiteSpace(ctx.MessageId), "the message has no id");
            received.Require(!string.IsNullOrWhiteSpace(ctx.SenderId), "the message has no sender id");
            received.Signal();
        };

        var setup = new HostSetup(bus => bus.AddSaga<SimpleSaga>())
        {
            ConfigureServices = services => services.AddSingleton(onMessage)
        };

        await RunScenarioAsync(combination, hostsCount, setup, async (run, cancellationToken) =>
        {
            await run.AnyHost.PublishAsync(message, cancellationToken);
            await received.Reached.WaitAsync(cancellationToken);
        });

        Assert.Empty(received.Problems);
        Assert.Equal(1, received.Count);
    }
}
