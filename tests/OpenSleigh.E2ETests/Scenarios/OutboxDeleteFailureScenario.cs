using System.Collections.Concurrent;
using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas;
using OpenSleigh.Outbox;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// the relay publishes a message and cannot remove it from the outbox, as when the database goes away in between:
/// the message is published again, and whoever receives it twice handles it once.
/// </summary>
public class OutboxDeleteFailureScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private const int Copies = 2;
    private static readonly TimeSpan MongoOutboxClaim = TimeSpan.FromSeconds(2);

    [Theory]
    [MatrixData(Only = [Combinations.InMemory], HostsCounts = [1])]
    public Task a_message_that_was_published_but_not_removed_is_published_again_and_handled_once(string combination, int hostsCount)
        => PublishWithoutRemovingAsync(combination, hostsCount);

    // the second copy can reach another host than the first, which only knows what is in the store.
    // What fails, and what tells the copies apart, is the store: every store runs it, and one combination has Kafka
    [Theory]
    [MatrixData(
        Only = [Combinations.SqlServerRabbitMq, Combinations.PostgreSqlRabbitMq, Combinations.MongoDbRabbitMq, Combinations.PostgreSqlKafka],
        HostsCounts = [3])]
    public Task a_message_that_was_published_but_not_removed_is_published_again_and_handled_once_with_several_hosts(string combination, int hostsCount)
        => PublishWithoutRemovingAsync(combination, hostsCount);

    private async Task PublishWithoutRemovingAsync(string combination, int hostsCount)
    {
        var correlationId = Guid.NewGuid().ToString();
        var outboxCannotBeCleaned = new Fault("the published messages cannot be removed from the outbox, on purpose.");
        var runs = new HandlerRuns();
        var published = new ConcurrentQueue<MessageEnvelope>();
        var copiesDelivered = new Expectation(Copies);

        var setup = new HostSetup(bus => bus.AddSaga<ShipmentSaga, ShipmentState>())
        {
            ConfigureServices = services => services
                .RecordHandlerRuns(runs)
                .ExpireOutboxClaimsAfter(MongoOutboxClaim)
                .FailOutboxDeletesWhile(outboxCannotBeCleaned)
                .ObservePublishedMessages(published.Enqueue)
                .ObserveDeliveriesOf<StartShipment>(copiesDelivered)
        };

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                outboxCannotBeCleaned.StartFor(failures: 1);
                await run.AnyHost.PublishNewAsync(new StartShipment(correlationId), cancellationToken);

                await run.WaitUntilAsync("the message that was not removed to be published again",
                    () => published.Count >= Copies, cancellationToken);

                // the hosts are done with both copies: the second one found nothing to do
                await run.WaitForAsync("both copies of the message to be delivered", copiesDelivered.Reached, cancellationToken);
                await run.WaitForReleasedSagaAsync<ShipmentSaga>(correlationId, handledMessages: 1, cancellationToken);
            },
            // a copy that arrives while the other is being handled finds the saga locked, and is tried again
            AssertOnlyLockConflictsAsync);

        // the delete failed, so the message really was published a second time: the same message, handled once
        Assert.Equal(1, outboxCannotBeCleaned.Hits);
        Assert.True(published.Count >= Copies);
        Assert.Single(published.Select(message => message.MessageId).Distinct());
        Assert.Single(runs.Runs);
    }
}
