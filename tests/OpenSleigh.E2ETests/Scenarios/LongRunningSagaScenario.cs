using Microsoft.Extensions.DependencyInjection;
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

    // one host, and a pair: with two, a step is rarely handled by the host that handled the previous one.
    // More hosts add nothing to a saga that has one message in flight at a time
    private const int OneHost = 1;
    private const int TwoHosts = 2;

    /// <summary>
    /// the order of the PizzaTracker sample: one event at a time from outside, each sent once the previous one is stored.
    /// </summary>
    [Theory]
    [MatrixData(HostsCounts = [OneHost, TwoHosts])]
    public async Task a_saga_is_moved_on_by_one_event_after_the_other(string combination, int hostsCount)
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
                await hosts[index % hosts.Count].PublishAsync(stageEvent, cancellationToken);

                await run.WaitForSagaAsync<PizzaOrderSaga>(correlationId,
                    saga => !saga.IsLocked && saga.StateData is PizzaOrderState state && state.Status == status,
                    cancellationToken);
            }

            var stored = await run.FindSagaAsync<PizzaOrderSaga>(correlationId, cancellationToken);
            Assert.NotNull(stored);
            Assert.True(stored.IsCompleted);
            Assert.Equal(stages.Length, stored.ProcessedMessages.Count);

            var state = Assert.IsType<PizzaOrderState>(stored.StateData);
            Assert.Equal("Ada Lovelace", state.Customer);
            Assert.Equal("Margherita", state.Pizza);
            Assert.Equal(stages.Select(stage => stage.Status), state.History);
        });
    }

    /// <summary>
    /// a subscription billed a hundred times, each cycle published by the previous one.
    /// </summary>
    [Theory]
    [MatrixData(HostsCounts = [OneHost, TwoHosts])]
    public async Task a_saga_goes_through_a_hundred_steps_without_losing_count(string combination, int hostsCount)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var lastCycleBilled = new Expectation(1);
        Action<IMessageContext<BillCycle>> onLastCycleBilled = _ => lastCycleBilled.Signal();

        var setup = new HostSetup(bus => bus.AddSaga<SubscriptionSaga, SubscriptionState>())
        {
            ConfigureServices = services => services.AddSingleton(onLastCycleBilled)
        };

        await RunScenarioAsync(combination, hostsCount, setup, async (run, cancellationToken) =>
        {
            await run.AnyHost.PublishAsync(new StartSubscription(correlationId, Cycles, Fee), cancellationToken);
            await lastCycleBilled.Reached.WaitAsync(cancellationToken);

            // the handler reports before its saga is stored
            var stored = await run.WaitForSagaAsync<SubscriptionSaga>(correlationId,
                saga => saga.IsCompleted && !saga.IsLocked, cancellationToken);

            var expected = new SubscriptionState
            {
                Cycles = Cycles,
                Fee = Fee,
                CyclesBilled = Cycles,
                TotalBilled = SubscriptionSaga.TotalOf(Cycles, Fee),
                CyclesOutOfTurn = 0,
            };
            Assert.Equal(expected, Assert.IsType<SubscriptionState>(stored.StateData));

            // the message that started it, and one per cycle
            Assert.Equal(Cycles + 1, stored.ProcessedMessages.Count);
            Assert.Equal(Cycles + 1, stored.ProcessedMessages.Select(message => message.MessageId).Distinct().Count());
        });

        Assert.Equal(1, lastCycleBilled.Count);
    }
}
