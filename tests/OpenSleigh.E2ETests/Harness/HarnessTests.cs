using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Harness.Stores;
using OpenSleigh.Outbox;
using OpenSleigh.Persistence;
using OpenSleigh.Transport;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Harness;

/// <summary>
/// the harness checking itself: the pieces the scenarios are built from, which no scenario uses yet.
/// </summary>
public class HarnessTests(ITestOutputHelper console) : ScenarioBase(console)
{
    private static readonly TimeSpan DeadLettersPollingInterval = TimeSpan.FromMilliseconds(100);

    [Fact]
    public void every_known_defect_excludes_at_least_one_case()
    {
        var cases = AllCases().ToArray();

        // the names the exclusions are written against are the ones the tests really have
        Assert.All(cases, matrixCase => Assert.Contains(matrixCase.Scenario, ScenarioClassNames()));

        foreach (var defect in KnownDefects.Exclusions)
        {
            var excluded = cases.Where(defect.Affects).ToArray();
            Console.WriteLine($"{defect.Id}: {defect.Reason}");
            foreach (var matrixCase in excluded)
                Console.WriteLine($"  excludes {matrixCase}");

            Assert.True(excluded.Length > 0, $"the known defect '{defect.Id}' excludes nothing: remove it.");
        }

        Console.WriteLine($"{cases.Length} cases, {cases.Count(c => KnownDefects.Find(c) is not null)} excluded.");
    }

    [Fact]
    public void the_matrix_has_every_store_with_every_transport_and_in_memory()
    {
        string[] expected =
        [
            Combinations.InMemory,
            Combinations.SqlServerRabbitMq,
            Combinations.SqlServerKafka,
            Combinations.PostgreSqlRabbitMq,
            Combinations.PostgreSqlKafka,
            Combinations.MongoDbRabbitMq,
            Combinations.MongoDbKafka,
        ];

        Assert.Equal(expected, Combinations.All.Select(combination => combination.Name));
    }

    [Fact]
    public void a_matrix_that_is_wrong_or_empty_fails_instead_of_being_skipped()
    {
        Assert.Throws<InvalidOperationException>(() => new MatrixDataAttribute { Only = ["postgresql_rabbitmq"] }.AllCases());
        Assert.Throws<InvalidOperationException>(() => new MatrixDataAttribute { Only = [] }.AllCases());
        Assert.Throws<InvalidOperationException>(() => new MatrixDataAttribute { Only = [Combinations.InMemory], HostsCounts = [2] }.AllCases());
        Assert.Throws<InvalidOperationException>(() => new MatrixDataAttribute { Only = [Combinations.InMemory], RequiresSharedInfrastructure = true }.AllCases());

        // hosts that share nothing take no part in a scenario that needs several
        var severalHosts = new MatrixDataAttribute { HostsCounts = [2, 5] }.AllCases();
        Assert.DoesNotContain(severalHosts, matrixCase => matrixCase.Combination == Combinations.InMemory);
        Assert.Equal(2 * (Combinations.All.Count - 1), severalHosts.Count);
    }

    [Fact]
    public async Task hosts_that_share_nothing_cannot_run_together_however_they_are_started()
    {
        var setup = new HostSetup(bus => bus.AddSaga<ChildSaga>());

        await RunScenarioAsync(Combinations.InMemory, async (run, _) =>
        {
            var first = await run.StartHostAsync(setup);
            await Assert.ThrowsAsync<InvalidOperationException>(() => run.StartHostAsync(setup));

            await first.StopAsync();
            await run.StartHostAsync(setup);
        });
    }

    [Theory]
    [MatrixData(Only = [Combinations.InMemory, Combinations.PostgreSqlRabbitMq, Combinations.PostgreSqlKafka], HostsCounts = [1])]
    public async Task a_decorated_service_replaces_the_one_of_the_library_and_the_last_host_of_a_service_can_be_replaced(
        string combination, int hostsCount)
    {
        var published = new Expectation(2);
        var bothHandled = new Expectation(2);
        Action<IMessageContext<StartMultiStartSaga>, ISagaInstance<MultiStartSagaState>> onStart = (_, _) => bothHandled.Signal();
        Action<IMessageContext<AlsoStartMultiStartSaga>, ISagaInstance<MultiStartSagaState>> onAlsoStart = (_, _) => { };

        var setup = new HostSetup(bus => bus.AddSaga<MultiStartSaga, MultiStartSagaState>())
        {
            ConfigureServices = services => services
                .AddSingleton(onStart)
                .AddSingleton(onAlsoStart)
                .Decorate<IPublisher>((_, inner) => new CountingPublisher(inner, published))
        };

        await RunScenarioAsync(combination, async (run, cancellationToken) =>
        {
            // a host that is stopped while it handles a message leaves it to be delivered again:
            // this one is stopped once it is done with the first message
            Task WaitUntilHandledAsync(string correlationId)
                => run.WaitForSagaAsync<MultiStartSaga>(correlationId,
                    stored => stored.ProcessedMessages.Count == 1 && !stored.IsLocked, cancellationToken);

            var first = (await run.StartHostsAsync(hostsCount, setup))[0];
            var handledByFirst = Guid.NewGuid().ToString();
            await first.PublishAsync(new StartMultiStartSaga(1, handledByFirst), cancellationToken);
            await WaitUntilHandledAsync(handledByFirst);

            await first.StopAsync();
            Assert.False(first.IsRunning);

            var second = await run.StartHostAsync(setup);
            Assert.Same(second, run.AnyHost);

            var handledBySecond = Guid.NewGuid().ToString();
            await second.PublishAsync(new StartMultiStartSaga(2, handledBySecond), cancellationToken);
            await WaitUntilHandledAsync(handledBySecond);
            await bothHandled.Reached.WaitAsync(cancellationToken);
            await published.Reached.WaitAsync(cancellationToken);

            Assert.Equal(2, run.Hosts.Count);
        });

        Assert.Equal(2, bothHandled.Count);
        Assert.Equal(2, published.Count);
    }

    [Theory]
    [MatrixData(Only = [Combinations.InMemory, Combinations.PostgreSqlRabbitMq, Combinations.PostgreSqlKafka], HostsCounts = [1])]
    public async Task a_message_for_a_saga_that_does_not_exist_is_read_as_a_dead_letter(string combination, int hostsCount)
    {
        // the child saga handles this message and is not started by it
        var setup = new HostSetup(bus => bus.AddSaga<ChildSaga>());

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                Assert.True(run.SupportsDeadLetters);
                Assert.Empty(await run.ReadDeadLettersAsync(cancellationToken));

                await run.AnyHost.PublishAsync(new ProcessChildSaga(), cancellationToken);

                while ((await run.ReadDeadLettersAsync(cancellationToken)).Count == 0)
                    await Task.Delay(DeadLettersPollingInterval, cancellationToken);
            },
            async run =>
            {
                await AssertOnlySagaNotFoundDeadLettersAsync(run, typeof(ProcessChildSaga));

                var failure = Assert.Single(run.ProcessingFailures);
                Assert.True(failure.IsSagaNotFound);
                Assert.False(failure.IsLockConflict);
            });
    }

    // every combination needs one for its outbox relay. MongoDB once had none, and no host on it could relay anything
    [Fact]
    public void every_combination_registers_a_transaction_manager()
    {
        Assert.All(Combinations.All, combination =>
        {
            var context = RunContext.Create();
            var services = new ServiceCollection();
            services.AddOpenSleigh(bus =>
            {
                combination.CreateStore(context).Configure(bus);
                combination.CreateTransport(context).Configure(bus, HostIdentity.DefaultService);
            });

            Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ITransactionManager));
        });
    }

    private static IEnumerable<string> ScenarioClassNames()
        => typeof(HarnessTests).Assembly.GetTypes()
            .Where(type => type.IsAssignableTo(typeof(ScenarioBase)))
            .Select(type => type.Name);

    private static IEnumerable<MatrixCase> AllCases()
        => from type in typeof(HarnessTests).Assembly.GetTypes()
           where !type.IsAbstract
           from method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
           let matrix = method.GetCustomAttribute<MatrixDataAttribute>()
           where matrix is not null
           from matrixCase in matrix.AllCases()
           select matrixCase;

    private sealed class CountingPublisher(IPublisher inner, Expectation published) : IPublisher
    {
        public async ValueTask PublishAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
        {
            await inner.PublishAsync(message, cancellationToken);
            published.Signal();
        }
    }
}
