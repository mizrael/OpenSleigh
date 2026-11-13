using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.Outbox;
using OpenSleigh.Transport;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// two services handle the same message, each with its own saga, and one of them finds its saga locked.
/// </summary>
public class RetryWithSeveralServicesScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private const string ServiceThatRetries = "billing";
    private const string OtherService = "shipping";
    private const string PostgreSqlWithRabbitMq = Combinations.PostgreSql + "-" + Combinations.RabbitMq;

    [Theory]
    [MatrixData(Only = [PostgreSqlWithRabbitMq], HostsCounts = [1])]
    public async Task a_message_retried_by_one_service_is_not_delivered_again_to_the_other(string combination, int hostsCount)
    {
        var deliveries = new ConcurrentDictionary<string, int>();
        var handled = new Expectation(2);
        Action<IMessageContext<StartSimpleSaga>> onMessage = _ => handled.Signal();

        HostSetup SetupOf(string service, Action<IBusConfigurator> configureBus, bool failsOnce) => new(configureBus)
        {
            Service = service,
            ConfigureServices = services => services
                .AddSingleton(onMessage)
                .Decorate<IMessageProcessor>((_, inner) => new CountingProcessor(inner, service, deliveries, failsOnce))
        };

        await using var run = CreateRun(combination);
        await run.StartHostsAsync(hostsCount, SetupOf(ServiceThatRetries, bus => bus.AddSaga<SimpleSaga>(), failsOnce: true));
        await run.StartHostsAsync(hostsCount, SetupOf(OtherService, bus => bus.AddSaga<OtherSimpleSaga>(), failsOnce: false));

        await RunWithTimeoutAsync(async cancellationToken =>
        {
            await run.AnyHost.PublishAsync(new StartSimpleSaga(), cancellationToken);
            await handled.Reached.WaitAsync(cancellationToken);
        });

        await ObserveAsync();
        await AssertNothingIsLeftBehindAsync(run);
        await run.StopHostsAsync();

        Assert.Equal(2, handled.Count);
        Assert.Equal(
            new Dictionary<string, int> { [ServiceThatRetries] = 2, [OtherService] = 1 }.OrderBy(entry => entry.Key),
            deliveries.OrderBy(entry => entry.Key));
    }

    /// <summary>counts what its service is given to process, and can refuse the first as if its saga were locked.</summary>
    private sealed class CountingProcessor(
        IMessageProcessor inner,
        string service,
        ConcurrentDictionary<string, int> deliveries,
        bool failsOnce) : IMessageProcessor
    {
        public ValueTask ProcessAsync(MessageEnvelope outboxMessage, CancellationToken cancellationToken = default)
        {
            var delivery = deliveries.AddOrUpdate(service, 1, (_, count) => count + 1);
            if (failsOnce && delivery == 1)
                throw new LockException("the saga is locked, on purpose.");

            return inner.ProcessAsync(outboxMessage, cancellationToken);
        }
    }
}
