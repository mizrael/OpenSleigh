using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.Outbox;
using OpenSleigh.Transport;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Harness;

/// <summary>
/// the harness checking itself: the pieces the scenarios are built from, which no scenario uses yet.
/// </summary>
public class HarnessTests(ITestOutputHelper console) : ScenarioBase(console)
{
    [Fact]
    public void every_known_defect_excludes_at_least_one_case()
    {
        var cases = AllCases().ToArray();

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
            Combinations.NameOf(Combinations.SqlServer, Combinations.RabbitMq),
            Combinations.NameOf(Combinations.SqlServer, Combinations.Kafka),
            Combinations.NameOf(Combinations.PostgreSql, Combinations.RabbitMq),
            Combinations.NameOf(Combinations.PostgreSql, Combinations.Kafka),
            Combinations.NameOf(Combinations.MongoDb, Combinations.RabbitMq),
            Combinations.NameOf(Combinations.MongoDb, Combinations.Kafka),
        ];

        Assert.Equal(expected, Combinations.All.Select(combination => combination.Name));
    }

    [Fact]
    public async Task a_decorated_service_replaces_the_one_of_the_library_and_a_stopped_host_can_be_replaced()
    {
        var published = new Expectation(2);
        var firstReceived = new Expectation(1);
        var bothReceived = new Expectation(2);
        Action<IMessageContext<StartSimpleSaga>> onMessage = _ =>
        {
            firstReceived.Signal();
            bothReceived.Signal();
        };

        var setup = new HostSetup(bus => bus.AddSaga<SimpleSaga>())
        {
            ConfigureServices = services => services
                .AddSingleton(onMessage)
                .Decorate<IPublisher>((_, inner) => new CountingPublisher(inner, published))
        };

        await using var run = CreateRun(Combinations.InMemory);

        var first = await run.StartHostAsync(setup);
        await RunWithTimeoutAsync(async cancellationToken =>
        {
            await first.PublishAsync(new StartSimpleSaga(), cancellationToken);
            await firstReceived.Reached.WaitAsync(cancellationToken);
        });

        await first.StopAsync();
        Assert.False(first.IsRunning);

        var second = await run.StartHostAsync(setup);
        Assert.Same(second, run.AnyHost);

        await RunWithTimeoutAsync(async cancellationToken =>
        {
            await second.PublishAsync(new StartSimpleSaga(), cancellationToken);
            await bothReceived.Reached.WaitAsync(cancellationToken);
            await published.Reached.WaitAsync(cancellationToken);
        });

        await run.StopHostsAsync();

        Assert.Equal(2, run.Hosts.Count);
        Assert.Equal(2, published.Count);
    }

    private static IEnumerable<MatrixCase> AllCases()
        => from type in typeof(HarnessTests).Assembly.GetTypes()
           where !type.IsAbstract
           from method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
           let matrix = method.GetCustomAttribute<MatrixDataAttribute>()
           where matrix is not null
           from matrixCase in matrix.GetCases(method)
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
