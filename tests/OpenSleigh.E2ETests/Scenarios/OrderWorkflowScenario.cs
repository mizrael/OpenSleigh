using OpenSleigh.DependencyInjection;
using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas.OrderWorkflow;
using OpenSleigh.Transport;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// the order workflow of the ECommerce sample: an orchestrator asks two services at once, collects their answers
/// on one saga instance, then asks a third. Several orders go through it; the answers of the first one are made
/// to arrive while the saga is busy with another of them.
/// </summary>
public class OrderWorkflowScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private const int OrdersCount = 10;
    private const int LinesOfAnOrder = 2;

    // the answers to the first order contend for its saga: one of them keeps it, on one host, and another has to
    // reach a host that is free. The warehouse answers once per line, so there are enough of them for that
    private const int LinesOfTheContendedOrder = 16;

    // PlaceOrder, CreditChecked and OrderShipped, besides one StockReserved per line
    private const int MessagesHandledBesidesTheLines = 3;

    private const int Replicas = 3;

    private const string Orders = "orders";
    private const string Payments = "payments";
    private const string Warehouse = "warehouse";
    private const string Shipping = "shipping";

    /// <summary>
    /// replicas of one application that has the four sagas. A single replica is left out: it takes the answers
    /// one at a time, on RabbitMQ and on Kafka alike, and they could not be made to meet.
    /// </summary>
    [Theory]
    [MatrixData(RequiresSharedInfrastructure = true, HostsCounts = [Replicas])]
    public Task orders_are_fulfilled_by_replicas_of_one_application(string combination, int hostsCount)
        => RunOnOneApplicationAsync(combination, hostsCount);

    /// <summary>
    /// one host, which handles the messages it reads together at the same time: the answers meet there as well.
    /// </summary>
    [Theory]
    [MatrixData(Only = [Combinations.InMemory], HostsCounts = [1])]
    public Task orders_are_fulfilled_by_one_application_in_memory(string combination, int hostsCount)
        => RunOnOneApplicationAsync(combination, hostsCount);

    private Task RunOnOneApplicationAsync(string combination, int hostsCount)
    {
        var workflow = new Workflow();
        var setup = workflow.SetupOf(HostIdentity.DefaultService, bus =>
        {
            bus.AddSaga<OrderSaga, OrderState>();
            bus.AddSaga<CreditCheckSaga>();
            bus.AddSaga<StockReservationSaga>();
            bus.AddSaga<ShippingSaga>();
        });

        return RunWorkflowAsync(combination, workflow, run => run.StartHostsAsync(hostsCount, setup));
    }

    /// <summary>
    /// the topology of the sample: every saga in a service of its own, each with its replicas. What it adds to the
    /// replicas of one application is up to the transport, a queue or a consumer group per service:
    /// each transport runs it, over the three stores.
    /// </summary>
    [Theory]
    [MatrixData(
        Only = [Combinations.SqlServerKafka, Combinations.PostgreSqlRabbitMq, Combinations.MongoDbKafka],
        HostsCounts = [2])]
    public Task orders_are_fulfilled_by_a_service_per_saga(string combination, int hostsCount)
    {
        var workflow = new Workflow();
        HostSetup[] services =
        [
            workflow.SetupOf(Orders, bus => bus.AddSaga<OrderSaga, OrderState>()),
            workflow.SetupOf(Payments, bus => bus.AddSaga<CreditCheckSaga>()),
            workflow.SetupOf(Warehouse, bus => bus.AddSaga<StockReservationSaga>()),
            workflow.SetupOf(Shipping, bus => bus.AddSaga<ShippingSaga>()),
        ];

        return RunWorkflowAsync(combination, workflow, async run =>
        {
            foreach (var service in services)
                await run.StartHostsAsync(hostsCount, service);
        });
    }

    private async Task RunWorkflowAsync(string combination, Workflow workflow, Func<ScenarioRun, Task> startHostsAsync)
    {
        var contended = NewOrder(number: 1, LinesOfTheContendedOrder);
        var others = Enumerable.Range(2, OrdersCount - 1).Select(number => NewOrder(number, LinesOfAnOrder)).ToArray();
        PlaceOrder[] orders = [contended, .. others];
        var runs = workflow.Runs;

        await RunScenarioAsync(combination,
            async (run, cancellationToken) =>
            {
                await startHostsAsync(run);
                var publisher = run.AnyHost;

                // the first order is kept in its first step, with nothing else on its way to a host:
                // from here on an in-memory host can be told to read the messages that follow together
                var placing = runs.HoldNext(handlerRun => handlerRun.MessageType == typeof(PlaceOrder));
                await publisher.PublishNewAsync(contended, cancellationToken);
                await run.WaitForAsync("the first order to reach its saga", placing.Entered, cancellationToken);

                using (workflow.Batch.Of(2))
                    await run.ForceLockConflictAsync(runs,
                        handlerRun => IsAnswerTo(contended, handlerRun),
                        () =>
                        {
                            placing.Release();
                            return Task.CompletedTask;
                        },
                        cancellationToken);

                await Task.WhenAll(others.Select(order => publisher.PublishNewAsync(order, cancellationToken)));

                await run.WaitUntilAsync($"the {OrdersCount} orders to be shipped",
                    () => runs.RunsOf<OrderShipped>().Count == OrdersCount, cancellationToken);

                foreach (var order in orders)
                    await AssertIsStoredAsFulfilledAsync(run, order, cancellationToken);
            },
            // an answer that finds its order busy with another one is retried
            AssertOnlyLockConflictsAsync);

        // also after the observation window: no handler ran twice for a message, and every order was completed once
        Assert.All(runs.RunsByMessage(), runsOfMessage => Assert.Equal(1, runsOfMessage.Value));
        Assert.Equal(
            orders.Select(order => order.CorrelationId).Order(),
            runs.RunsOf<OrderShipped>().Select(handlerRun => handlerRun.CorrelationId).Order());
    }

    private static PlaceOrder NewOrder(int number, int lines)
        => new(
            Guid.NewGuid(),
            Guid.NewGuid().ToString("N"),
            Amount: 10m * number,
            Skus: Enumerable.Range(1, lines).Select(line => $"SKU-{number}-{line}").ToArray());

    private static bool IsAnswerTo(PlaceOrder order, HandlerRun handlerRun)
        => handlerRun.CorrelationId == order.CorrelationId
           && (handlerRun.MessageType == typeof(CreditChecked) || handlerRun.MessageType == typeof(StockReserved));

    private static async Task AssertIsStoredAsFulfilledAsync(ScenarioRun run, PlaceOrder order, CancellationToken cancellationToken)
    {
        var handledMessages = MessagesHandledBesidesTheLines + order.Skus.Count;
        var saga = await run.WaitForReleasedSagaAsync<OrderSaga>(order.CorrelationId, handledMessages, cancellationToken);
        Assert.True(saga.IsCompleted);
        Assert.Equal(handledMessages, saga.ProcessedMessages.Select(message => message.MessageId).Distinct().Count());

        var state = Assert.IsType<OrderState>(saga.StateData);
        Assert.Equal(order.OrderId, state.OrderId);
        Assert.Equal(order.Amount, state.Amount);
        Assert.True(state.CreditChecked);
        Assert.Equal(order.Skus.Order(), state.ReservedSkus.Order());
        Assert.Equal(1, state.ShipmentsRequested);
        Assert.Equal(ShippingSaga.TrackingNumberOf(order.OrderId), state.TrackingNumber);

        Assert.True((await run.WaitForReleasedSagaAsync<CreditCheckSaga>(order.CorrelationId, handledMessages: 1, cancellationToken)).IsCompleted);
        Assert.True((await run.WaitForReleasedSagaAsync<StockReservationSaga>(order.CorrelationId, handledMessages: 1, cancellationToken)).IsCompleted);
        Assert.True((await run.WaitForReleasedSagaAsync<ShippingSaga>(order.CorrelationId, handledMessages: 1, cancellationToken)).IsCompleted);
    }

    /// <summary>what the hosts of one run have in common, whatever their service.</summary>
    private sealed class Workflow
    {
        public HandlerRuns Runs { get; } = new();

        public InMemoryBatch Batch { get; } = new();

        public HostSetup SetupOf(string service, Action<IBusConfigurator> configureBus)
            => new(configureBus)
            {
                Service = service,
                ConfigureServices = services => services.RecordHandlerRuns(Runs).ReadInMemoryMessagesIn(Batch)
            };
    }
}
