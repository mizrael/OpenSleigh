using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.E2ETests.Sagas;
using OpenSleigh.Outbox;
using OpenSleigh.Queries;
using OpenSleigh.Transport;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// several messages that can each start the same saga are published together, from every host:
/// two of the same type and one of another. One of them starts the saga, the others find it started.
/// </summary>
public class SagaStartRaceScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    // enough carts for the race to happen in every run, on whatever store
    private const int Carts = 10;
    private const int MessagesPerCart = 3;

    private const string FirstSku = "sku-1";
    private const string SecondSku = "sku-2";
    private const string CouponCode = "WELCOME";

    // the hosts that race to start the saga. One host of these transports handles its messages one at a time,
    // and shows nothing that five hosts do not
    [Theory]
    [MatrixData(RequiresSharedInfrastructure = true, HostsCounts = [5])]
    public Task messages_that_start_the_same_saga_together_start_it_once_on_hosts_that_race(string combination, int hostsCount)
        => RunAsync(combination, hostsCount);

    // in memory a host handles several messages at the same time: it races with itself
    [Theory]
    [MatrixData(Only = [Combinations.InMemory], HostsCounts = [1])]
    public Task messages_that_start_the_same_saga_together_start_it_once_on_a_host_that_handles_several_at_once(string combination, int hostsCount)
        => RunAsync(combination, hostsCount);

    private async Task RunAsync(string combination, int hostsCount)
    {
        var carts = Enumerable.Range(0, Carts).Select(_ => Guid.NewGuid().ToString()).ToArray();
        var runs = new HandlerRuns(Carts * MessagesPerCart);

        var setup = new HostSetup(bus => bus.AddSaga<CartSaga, CartState>())
        {
            ConfigureServices = services => services.AddSingleton(runs)
        };

        var stored = new List<SagaInstanceInfo>();

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                var hosts = run.Hosts;
                var messages = carts.SelectMany(MessagesOf).ToArray();
                await Task.WhenAll(messages.Select((message, index)
                    => PublishAsync(hosts[index % hosts.Count], message, cancellationToken)));

                await runs.AllHandled.WaitAsync(cancellationToken);

                foreach (var cart in carts)
                    stored.Add(await run.WaitForSagaAsync<CartSaga>(cart,
                        saga => saga.ProcessedMessages.Count == MessagesPerCart && !saga.IsLocked,
                        cancellationToken));
            },
            async run =>
            {
                // every message starts the saga: none can be early, and none is given up on
                await AssertNoDeadLettersAsync(run);

                // whoever loses the race is given the message again
                Assert.All(run.ProcessingFailures, failure => Assert.True(failure.IsLockConflict, failure.ToString()));
            });

        Assert.All(runs.RunsByMessage(), runsOfMessage => Assert.Equal(1, runsOfMessage.Value));
        Assert.Equal(Carts * MessagesPerCart, runs.Runs.Count);

        Assert.All(runs.Runs.GroupBy(run => run.CorrelationId),
            runsOfCart => Assert.Single(runsOfCart.Select(run => run.InstanceId).Distinct()));

        Assert.All(stored, cart =>
        {
            var state = Assert.IsType<CartState>(cart.StateData);
            Assert.Equal([FirstSku, SecondSku], state.Skus.Order());
            Assert.Equal([CouponCode], state.Coupons);
        });
    }

    private static IEnumerable<IMessage> MessagesOf(string cart)
        => [new AddItemToCart(cart, FirstSku), new ApplyCoupon(cart, CouponCode), new AddItemToCart(cart, SecondSku)];

    private static async Task PublishAsync(ScenarioHost host, IMessage message, CancellationToken cancellationToken)
        => Assert.Equal(OutboxAppendResult.Success, await host.PublishAsync(message, cancellationToken));
}
