using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.E2ETests.Sagas.OrderWorkflow;
using OpenSleigh.Queries;
using OpenSleigh.Transport;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// the order workflow of the ECommerce sample: an orchestrator asks two services at once, waits for both answers
/// on one saga instance, then asks a third. Several orders go through it at the same time.
/// </summary>
public class OrderWorkflowScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private const int OrdersCount = 10;

    // PlaceOrder, CreditChecked, StockReserved and OrderShipped
    private const int MessagesHandledByAnOrder = 4;

    private const string Orders = "orders";
    private const string Payments = "payments";
    private const string Warehouse = "warehouse";
    private const string Shipping = "shipping";

    /// <summary>
    /// replicas of one application that has the four sagas: the two answers reach different hosts at the same time.
    /// </summary>
    [Theory]
    [MatrixData]
    public Task orders_are_fulfilled_by_replicas_of_one_application(string combination, int hostsCount)
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
    /// the topology of the sample: every saga in a service of its own, each with its replicas.
    /// </summary>
    [Theory]
    [MatrixData(RequiresSharedInfrastructure = true, HostsCounts = [2])]
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
        var orders = Enumerable.Range(1, OrdersCount)
            .Select(number => new PlaceOrder(Guid.NewGuid(), Guid.NewGuid().ToString("N"), Amount: 10m * number, Quantity: number))
            .ToArray();

        await RunScenarioAsync(combination, async (run, cancellationToken) =>
        {
            await startHostsAsync(run);

            var publisher = run.AnyHost;
            await Task.WhenAll(orders.Select(order => publisher.PublishAsync(order, cancellationToken)));

            await workflow.Completed.Reached.WaitAsync(cancellationToken);

            foreach (var order in orders)
                await AssertIsStoredAsFulfilledAsync(run, order, cancellationToken);
        });

        // also after the observation window: nothing completed an order a second time
        Assert.Equal(
            orders.Select(order => order.OrderId).Order(),
            workflow.CompletedOrders.Order());
    }

    private static async Task AssertIsStoredAsFulfilledAsync(ScenarioRun run, PlaceOrder order, CancellationToken cancellationToken)
    {
        // the handler reports before its saga is stored
        var saga = await run.WaitForSagaAsync<OrderSaga>(order.CorrelationId, IsCompletedAndReleased, cancellationToken);

        var expected = new OrderState
        {
            OrderId = order.OrderId,
            Amount = order.Amount,
            Quantity = order.Quantity,
            CreditChecked = true,
            StockReserved = true,
            ShipmentsRequested = 1,
            TrackingNumber = ShippingSaga.TrackingNumberOf(order.OrderId),
        };
        Assert.Equal(expected, Assert.IsType<OrderState>(saga.StateData));
        Assert.Equal(MessagesHandledByAnOrder, saga.ProcessedMessages.Select(message => message.MessageId).Distinct().Count());
        Assert.Equal(MessagesHandledByAnOrder, saga.ProcessedMessages.Count);

        await run.WaitForSagaAsync<CreditCheckSaga>(order.CorrelationId, IsCompletedAndReleased, cancellationToken);
        await run.WaitForSagaAsync<StockReservationSaga>(order.CorrelationId, IsCompletedAndReleased, cancellationToken);
        await run.WaitForSagaAsync<ShippingSaga>(order.CorrelationId, IsCompletedAndReleased, cancellationToken);
    }

    private static bool IsCompletedAndReleased(SagaInstanceInfo saga) => saga.IsCompleted && !saga.IsLocked;

    /// <summary>what the hosts of one run report to.</summary>
    private sealed class Workflow
    {
        private readonly ConcurrentQueue<Guid> _completedOrders = new();

        public Expectation Completed { get; } = new(OrdersCount);

        /// <summary>one entry for every time an order was completed.</summary>
        public IReadOnlyCollection<Guid> CompletedOrders => _completedOrders;

        public HostSetup SetupOf(string service, Action<IBusConfigurator> configureBus)
        {
            Action<IMessageContext<OrderShipped>> onCompleted = context =>
            {
                _completedOrders.Enqueue(context.Message.OrderId);
                Completed.Signal();
            };

            return new HostSetup(configureBus)
            {
                Service = service,
                ConfigureServices = services => services.AddSingleton(onCompleted)
            };
        }
    }
}
