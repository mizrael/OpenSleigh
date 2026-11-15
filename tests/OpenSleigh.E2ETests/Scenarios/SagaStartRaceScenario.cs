using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas;
using OpenSleigh.Queries;
using OpenSleigh.Transport;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// several messages that can each start the same saga are published together, from every host, of two types.
/// One of them starts the saga, the others find it started.
/// </summary>
public class SagaStartRaceScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    // enough messages for another host to be given one while the first waits for it, on whatever transport
    private static readonly Cart ContendedCart = new(Items: 12, Coupons: 2);
    private static readonly Cart OtherCart = new(Items: 2, Coupons: 1);
    private const int OtherCarts = 5;
    private static readonly int Messages = ContendedCart.Messages + OtherCarts * OtherCart.Messages;

    // the hosts that race to start the saga: one host of these transports handles its messages one at a time.
    // Storing a saga once is up to the store and the retry of the loser up to the transport, so every store runs
    // with one transport and one store with the other
    [Theory]
    [MatrixData(
        Only = [Combinations.SqlServerRabbitMq, Combinations.PostgreSqlRabbitMq, Combinations.MongoDbRabbitMq, Combinations.PostgreSqlKafka],
        HostsCounts = [5])]
    public Task messages_that_start_the_same_saga_together_start_it_once_on_hosts_that_race(string combination, int hostsCount)
        => RunAsync(combination, hostsCount);

    // in memory a host handles the messages it reads together at the same time: it races with itself
    [Theory]
    [MatrixData(Only = [Combinations.InMemory], HostsCounts = [1])]
    public Task messages_that_start_the_same_saga_together_start_it_once_on_a_host_that_handles_several_at_once(string combination, int hostsCount)
        => RunAsync(combination, hostsCount);

    private async Task RunAsync(string combination, int hostsCount)
    {
        var contended = Guid.NewGuid().ToString();
        var others = Enumerable.Range(0, OtherCarts).Select(_ => Guid.NewGuid().ToString()).ToArray();

        var runs = new HandlerRuns();
        var batch = new InMemoryBatch();
        var race = new StartRace(contended);

        var setup = new HostSetup(bus => bus.AddSaga<CartSaga, CartState>())
        {
            ConfigureServices = services => services
                .RecordHandlerRuns(runs)
                .ReadInMemoryMessagesIn(batch)
                .Intercept(_ => new StartsWhenAnotherStarts(race))
        };

        var stored = new Dictionary<string, SagaInstanceInfo>();

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                // one cart is started by two messages at once, for sure: neither goes on before both have looked
                // for it and found nothing. The others are started by whatever reaches them first
                using (batch.Of(InMemoryBatch.MaxSize))
                    await run.ForceLockConflictAsync(runs,
                        handlerRun => handlerRun.CorrelationId == contended,
                        () => PublishFromEveryHostAsync(run, ContendedCart.MessagesFor(contended), cancellationToken),
                        cancellationToken);

                await PublishFromEveryHostAsync(run, others.SelectMany(OtherCart.MessagesFor), cancellationToken);

                await run.WaitUntilAsync($"the {Messages} messages to be handled",
                    () => runs.HandledMessages == Messages, cancellationToken);

                stored[contended] = await run.WaitForReleasedSagaAsync<CartSaga>(contended, ContendedCart.Messages, cancellationToken);
                foreach (var cart in others)
                    stored[cart] = await run.WaitForReleasedSagaAsync<CartSaga>(cart, OtherCart.Messages, cancellationToken);
            },
            // every message starts the saga: none can be early. Whoever loses the race is given the message again
            AssertOnlyLockConflictsAsync);

        Assert.True(race.Happened, "no two messages looked for the cart before it was stored.");

        Assert.All(runs.RunsByMessage(), runsOfMessage => Assert.Equal(1, runsOfMessage.Value));
        Assert.Equal(Messages, runs.Runs.Count);

        Assert.All(runs.Runs.GroupBy(run => run.CorrelationId),
            runsOfCart => Assert.Single(runsOfCart.Select(run => run.InstanceId).Distinct()));

        AssertHolds(ContendedCart, stored[contended]);
        Assert.All(others, cart => AssertHolds(OtherCart, stored[cart]));
    }

    // what each message wrote is in the state, whichever was handled first
    private static void AssertHolds(Cart expected, SagaInstanceInfo stored)
    {
        var state = Assert.IsType<CartState>(stored.StateData);
        Assert.Equal(expected.Skus, state.Skus.Order());
        Assert.Equal(expected.Codes, state.Coupons.Order());
    }

    private static Task PublishFromEveryHostAsync(ScenarioRun run, IEnumerable<IMessage> messages, CancellationToken cancellationToken)
    {
        var hosts = run.Hosts;
        return Task.WhenAll(messages.Select((message, index)
            => hosts[index % hosts.Count].PublishNewAsync(message, cancellationToken)));
    }

    private sealed record Cart(int Items, int Coupons)
    {
        public int Messages => Items + Coupons;

        public IEnumerable<string> Skus => Enumerable.Range(0, Items).Select(index => $"sku-{index:00}");

        public IEnumerable<string> Codes => Enumerable.Range(0, Coupons).Select(index => $"coupon-{index:00}");

        public IEnumerable<IMessage> MessagesFor(string cart)
            => Skus.Select<string, IMessage>(sku => new AddItemToCart(cart, sku))
                .Concat(Codes.Select(code => new ApplyCoupon(cart, code)));
    }

    /// <summary>two messages that found no saga for a cart, before either stored one. One for all the hosts.</summary>
    private sealed class StartRace(string cart)
    {
        private const int Starters = 2;

        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _foundNothing;

        public string Cart => cart;

        public bool Happened => _started.Task.IsCompleted;

        public Task WaitForAnotherStarterAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _foundNothing) >= Starters)
                _started.TrySetResult();

            return _started.Task.WaitAsync(cancellationToken);
        }
    }

    /// <summary>a message that finds no saga for the cart waits for another one that does not either.</summary>
    private sealed class StartsWhenAnotherStarts(StartRace race) : SagaStateRepositoryInterceptor
    {
        public override async ValueTask<ISagaInstance?> FindAsync<TM>(
            ISagaStateRepository repository,
            SagaDescriptor descriptor,
            IMessageContext<TM> messageContext,
            CancellationToken cancellationToken)
        {
            var found = await base.FindAsync(repository, descriptor, messageContext, cancellationToken);
            if (found is null && messageContext.CorrelationId == race.Cart)
                await race.WaitForAnotherStarterAsync(cancellationToken);

            return found;
        }
    }
}
