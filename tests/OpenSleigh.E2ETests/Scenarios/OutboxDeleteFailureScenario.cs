using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.Transport;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// the relay publishes a message and cannot remove it from the outbox, as when the database goes away in between:
/// the message is published again, and whoever receives it twice handles it once.
/// </summary>
public class OutboxDeleteFailureScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private static readonly TimeSpan MongoOutboxClaim = TimeSpan.FromSeconds(2);

    // in memory, where there is one host, and with a few hosts everywhere else: the second copy can reach
    // another host than the first, which only knows what is in the store
    [Theory]
    [MatrixData(Only = [Combinations.InMemory], HostsCounts = [1])]
    public Task a_message_that_was_published_but_not_removed_is_published_again_and_handled_once(string combination, int hostsCount)
        => PublishWithoutRemovingAsync(combination, hostsCount);

    [Theory]
    [MatrixData(HostsCounts = [3])]
    public Task a_message_that_was_published_but_not_removed_is_published_again_and_handled_once_with_several_hosts(string combination, int hostsCount)
        => PublishWithoutRemovingAsync(combination, hostsCount);

    private async Task PublishWithoutRemovingAsync(string combination, int hostsCount)
    {
        var outboxCannotBeCleaned = new Fault("the published messages cannot be removed from the outbox, on purpose.");
        var publishedTwice = new Expectation(2);
        var deliveredTwice = new Expectation(2);
        var handled = new Expectation(1);
        Action<IMessageContext<StartSimpleSaga>> onMessage = _ => handled.Signal();

        var setup = new HostSetup(bus => bus.AddSaga<SimpleSaga>())
        {
            ConfigureServices = services => services
                .AddSingleton(onMessage)
                .ExpireOutboxClaimsAfter(MongoOutboxClaim)
                .FailOutboxDeletesWhile(outboxCannotBeCleaned)
                .ObservePublishedMessages(_ => publishedTwice.Signal())
                .ObserveDeliveriesOf<StartSimpleSaga>(deliveredTwice)
        };

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                outboxCannotBeCleaned.StartFor(failures: 1);
                await run.AnyHost.PublishAsync(new StartSimpleSaga(), cancellationToken);

                await publishedTwice.Reached.WaitAsync(cancellationToken);
                await handled.Reached.WaitAsync(cancellationToken);

                // the hosts are done with both copies: the second one found nothing to do
                await deliveredTwice.Reached.WaitAsync(cancellationToken);
            },
            async run =>
            {
                await AssertNoDeadLettersAsync(run);

                // a copy that arrives while the other is being handled finds the saga locked, and is tried again
                Assert.All(run.ProcessingFailures, failure => Assert.True(failure.IsLockConflict, failure.Exception.ToString()));
            });

        Assert.Single(outboxCannotBeCleaned.Hits);
        Assert.Equal(2, publishedTwice.Count);
        Assert.Equal(2, deliveredTwice.Count);
        Assert.Equal(1, handled.Count);
    }
}
