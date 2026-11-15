using OpenSleigh.E2ETests.Sagas;
using OpenSleigh.Transport;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// sagas that handle many messages in their life: the list of the messages a saga has processed grows with each,
/// and the state every step starts from is the one the previous step stored.
/// </summary>
public class LongRunningSagaScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private const int Cycles = 100;
    private const decimal Fee = 9.99m;

    // the message that starts the subscription, and one per cycle
    private const int MessagesHandledByASubscription = Cycles + 1;

    // a pair of hosts: a step is rarely handled by the host that handled the previous one.
    // More hosts add nothing to a saga that has one message in flight at a time, and one host is what in-memory has
    private const int TwoHosts = 2;

    /// <summary>
    /// the order of the PizzaTracker sample: one event at a time from outside, each sent once the previous one is stored.
    /// Loading a saga with what it has handled so far, and storing it again, is code of the store:
    /// every store runs it, and one combination has Kafka.
    /// </summary>
    [Theory]
    [MatrixData(
        Only = [Combinations.SqlServerRabbitMq, Combinations.PostgreSqlRabbitMq, Combinations.MongoDbRabbitMq, Combinations.PostgreSqlKafka],
        HostsCounts = [TwoHosts])]
    public Task a_saga_is_moved_on_by_one_event_after_the_other(string combination, int hostsCount)
        => TrackPizzaOrderAsync(combination, hostsCount);

    [Theory]
    [MatrixData(Only = [Combinations.InMemory], HostsCounts = [1])]
    public Task a_saga_is_moved_on_by_one_event_after_the_other_in_memory(string combination, int hostsCount)
        => TrackPizzaOrderAsync(combination, hostsCount);

    /// <summary>
    /// a subscription billed a hundred times, each cycle published by the previous one. On the same combinations.
    /// </summary>
    [Theory]
    [MatrixData(
        Only = [Combinations.SqlServerRabbitMq, Combinations.PostgreSqlRabbitMq, Combinations.MongoDbRabbitMq, Combinations.PostgreSqlKafka],
        HostsCounts = [TwoHosts])]
    public Task a_saga_goes_through_a_hundred_steps_without_losing_count(string combination, int hostsCount)
        => BillSubscriptionAsync(combination, hostsCount);

    [Theory]
    [MatrixData(Only = [Combinations.InMemory], HostsCounts = [1])]
    public Task a_saga_goes_through_a_hundred_steps_without_losing_count_in_memory(string combination, int hostsCount)
        => BillSubscriptionAsync(combination, hostsCount);

    private async Task TrackPizzaOrderAsync(string combination, int hostsCount)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        (IMessage Event, string Status)[] stages =
        [
            (new PlacePizzaOrder(correlationId, "Ada Lovelace", "Margherita"), PizzaOrderSaga.Received),
            (new PizzaPrepared(correlationId), PizzaOrderSaga.Prepared),
            (new PizzaBaked(correlationId), PizzaOrderSaga.Baked),
            (new PizzaChecked(correlationId), PizzaOrderSaga.Checked),
            (new PizzaOutForDelivery(correlationId), PizzaOrderSaga.OutForDelivery),
            (new PizzaDelivered(correlationId), PizzaOrderSaga.Delivered),
        ];

        var setup = new HostSetup(bus => bus.AddSaga<PizzaOrderSaga, PizzaOrderState>());

        await RunScenarioAsync(combination, hostsCount, setup, async (run, cancellationToken) =>
        {
            var hosts = run.Hosts;
            for (var index = 0; index < stages.Length; index++)
            {
                var (stageEvent, status) = stages[index];

                // each event through another host, the way requests reach the replicas of an API
                await hosts[index % hosts.Count].PublishNewAsync(stageEvent, cancellationToken);

                var moved = await run.WaitForReleasedSagaAsync<PizzaOrderSaga>(correlationId, handledMessages: index + 1, cancellationToken);
                Assert.Equal(status, Assert.IsType<PizzaOrderState>(moved.StateData).Status);
            }

            var stored = await run.WaitForReleasedSagaAsync<PizzaOrderSaga>(correlationId, stages.Length, cancellationToken);
            Assert.True(stored.IsCompleted);

            var state = Assert.IsType<PizzaOrderState>(stored.StateData);
            Assert.Equal("Ada Lovelace", state.Customer);
            Assert.Equal("Margherita", state.Pizza);
            Assert.Equal(stages.Select(stage => stage.Status), state.History);
        });
    }

    private async Task BillSubscriptionAsync(string combination, int hostsCount)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var setup = new HostSetup(bus => bus.AddSaga<SubscriptionSaga, SubscriptionState>());

        await RunScenarioAsync(combination, hostsCount, setup, async (run, cancellationToken) =>
        {
            await run.AnyHost.PublishNewAsync(new StartSubscription(correlationId, Cycles, Fee), cancellationToken);

            var stored = await run.WaitForReleasedSagaAsync<SubscriptionSaga>(correlationId, MessagesHandledByASubscription, cancellationToken);
            Assert.True(stored.IsCompleted);
            Assert.Equal(MessagesHandledByASubscription, stored.ProcessedMessages.Select(message => message.MessageId).Distinct().Count());

            // a cycle billed twice, or on a state that misses the one before it, shows in the count, the total or the turns
            var expected = new SubscriptionState
            {
                Cycles = Cycles,
                Fee = Fee,
                CyclesBilled = Cycles,
                TotalBilled = SubscriptionSaga.TotalOf(Cycles, Fee),
                CyclesOutOfTurn = 0,
            };
            Assert.Equal(expected, Assert.IsType<SubscriptionState>(stored.StateData));
        });
    }
}
