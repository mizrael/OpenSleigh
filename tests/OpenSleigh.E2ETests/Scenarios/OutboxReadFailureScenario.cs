using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.Transport;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// the relay cannot read its outbox for a while, as when the database does not answer: it is still there afterwards.
/// </summary>
public class OutboxReadFailureScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private const int FailedPassesToWaitFor = 3;

    // the relay of these tests reads its outbox every 250 ms, and waits that long after a pass that failed.
    // One that tried again without waiting would be back within a few milliseconds
    private static readonly TimeSpan ShortestPauseOfARelayThatBacksOff = TimeSpan.FromMilliseconds(200);

    // one host: a relay that stops for good after a failed pass is the relay of one host, however many there are
    [Theory]
    [MatrixData(HostsCounts = [1])]
    public async Task a_relay_that_cannot_read_its_outbox_keeps_trying_and_relays_what_is_in_it_afterwards(string combination, int hostsCount)
    {
        var outboxCannotBeRead = new Fault("the outbox cannot be read, on purpose.");
        var handled = new Expectation(1);
        Action<IMessageContext<StartSimpleSaga>> onMessage = _ => handled.Signal();

        var setup = new HostSetup(bus => bus.AddSaga<SimpleSaga>())
        {
            ConfigureServices = services => services
                .AddSingleton(onMessage)
                .FailOutboxReadsWhile(outboxCannotBeRead)
        };

        await RunScenarioAsync(combination, hostsCount, setup, async (run, cancellationToken) =>
        {
            outboxCannotBeRead.Start();
            await run.AnyHost.PublishAsync(new StartSimpleSaga(), cancellationToken);

            await outboxCannotBeRead.WaitForHitsAsync(FailedPassesToWaitFor, cancellationToken);
            Assert.Equal(0, handled.Count);

            outboxCannotBeRead.Stop();
            await handled.Reached.WaitAsync(cancellationToken);
        });

        Assert.Equal(1, handled.Count);

        var shortestPause = outboxCannotBeRead.ShortestTimeBetweenHitsOfOneSource();
        Console.WriteLine($"MEASURED outbox-read-failure passes={outboxCannotBeRead.Hits.Count} shortest-pause-ms={shortestPause?.TotalMilliseconds:F0}");
        Assert.True(shortestPause >= ShortestPauseOfARelayThatBacksOff,
            $"the relay read its outbox again {shortestPause?.TotalMilliseconds} ms after it failed: it does not back off.");
    }
}
