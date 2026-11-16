using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.DependencyInjection;
using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.Outbox;
using OpenSleigh.Transport;
using OpenSleigh.Transport.RabbitMQ;
using RabbitMQ.Client;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// an API that only publishes is deployed before the workers that consume its messages have ever started:
/// no queue is bound to the exchange it publishes to, and the broker returns its messages.
/// What happens to them is up to the transport and to the outbox of each store.
/// </summary>
public class ReturnedMessageScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private const string Api = "api";

    // published once, returned, and published again from the outbox at least once
    private const int PublishesOfAMessageThatCameBack = 2;
    private static readonly TimeSpan DeadLettersPollingInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ClockMargin = TimeSpan.FromSeconds(1);

    [Theory]
    [MatrixData(Only = [Combinations.SqlServerRabbitMq, Combinations.PostgreSqlRabbitMq, Combinations.MongoDbRabbitMq], HostsCounts = [1])]
    public async Task a_message_published_before_its_consumer_ever_started_is_delivered_once_when_it_starts(string combination, int hostsCount)
    {
        var message = new StartSimpleSaga();
        var received = new Expectation(1);
        var publishes = new Publishes();

        await RunScenarioAsync(combination,
            async (run, cancellationToken) =>
            {
                var api = await run.StartHostAsync(ApiSetup(publishes));

                await api.PublishAsync(message, cancellationToken);
                await run.WaitUntilAsync("the message to come back and be published again",
                    () => publishes.Count >= PublishesOfAMessageThatCameBack, cancellationToken);

                await run.StartHostsAsync(hostsCount, WorkerSetup(received));
                await received.Reached.WaitAsync(cancellationToken);
            });

        Assert.Equal(1, received.Count);
    }

    // the API is the only host: hostsCount is what the matrix gives every scenario
    [Theory]
    [MatrixData(Only = [Combinations.SqlServerRabbitMq, Combinations.PostgreSqlRabbitMq, Combinations.MongoDbRabbitMq], HostsCounts = [1])]
    public async Task a_message_that_still_comes_back_when_its_period_is_over_is_dead_lettered_and_not_published_again(string combination, int hostsCount)
    {
        _ = hostsCount;
        var message = new StartSimpleSaga();
        var publishes = new Publishes();
        var clock = new Clock();
        var publishesWhenDeadLettered = 0;

        await RunScenarioAsync(combination,
            async (run, cancellationToken) =>
            {
                var api = await run.StartHostAsync(ApiSetup(publishes, clock));

                // someone who wants to keep what the API gives up on: the API declares no queue, this one included
                await DeclareDeadLetterQueueAsync(api, cancellationToken);

                await api.PublishAsync(message, cancellationToken);
                await run.WaitUntilAsync("the message to come back and be published again",
                    () => publishes.Count >= PublishesOfAMessageThatCameBack, cancellationToken);

                // from here on the message is past its period whenever it is handled: it was put back at least once,
                // so it carries the time it first came back, which is before the clock moved. The period is looked at
                // on the same reading of the clock as the due time, so nothing is put back with a due time on the moved
                // clock, which the relay, on the system clock, would wait minutes for
                clock.Advance(api.Services.GetRequiredService<RabbitConfiguration>().ReturnedMessageRetryPeriod + ClockMargin);

                while (!(await run.ReadDeadLettersAsync(cancellationToken)).ContainsKey(typeof(StartSimpleSaga)))
                    await Task.Delay(DeadLettersPollingInterval, cancellationToken);
                publishesWhenDeadLettered = publishes.Count;
            },
            async run => Assert.Equal(
                new Dictionary<Type, long> { [typeof(StartSimpleSaga)] = 1 },
                await run.ReadDeadLettersAsync(CancellationToken.None)));

        // the observation window is several retry delays long
        Assert.Equal(publishesWhenDeadLettered, publishes.Count);
    }

    private static HostSetup WorkerSetup(Expectation received)
    {
        Action<IMessageContext<StartSimpleSaga>> onMessage = _ => received.Signal();

        return new HostSetup(bus => bus.AddSaga<SimpleSaga>())
        {
            ConfigureServices = services => services.AddSingleton(onMessage)
        };
    }

    private static HostSetup ApiSetup(Publishes publishes, TimeProvider? clock = null)
        => new(bus => bus.AddSaga<SimpleSaga>().SetPublishOnly())
        {
            Service = Api,
            ConfigureServices = services =>
            {
                services.Decorate<IPublisher>((_, inner) => new CountingPublisher(inner, publishes));

                if (clock is not null)
                    services.AddSingleton(clock);
            }
        };

    // as a host that consumes the message declares them, on a channel of its own
    private static async Task DeclareDeadLetterQueueAsync(ScenarioHost api, CancellationToken cancellationToken)
    {
        var configuration = api.Services.GetRequiredService<RabbitConfiguration>();
        var references = api.Services.GetRequiredService<IQueueReferenceFactory>().Create(typeof(StartSimpleSaga));

        await using var channel = await api.Services.GetRequiredService<IBusConnection>().CreateChannelAsync(cancellationToken);
        await channel.ExchangeDeclareAsync(
            references.DeadLetterExchangeName, ExchangeType.Topic, configuration.Durable, configuration.AutoDelete, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(
            references.DeadLetterQueue, configuration.Durable, exclusive: false, configuration.AutoDelete, arguments: null, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            references.DeadLetterQueue, references.DeadLetterExchangeName, routingKey: references.DeadLetterQueue, arguments: null, cancellationToken: cancellationToken);
    }

    private sealed class Publishes
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Add() => Interlocked.Increment(ref _count);
    }

    private sealed class CountingPublisher(IPublisher inner, Publishes publishes) : IPublisher
    {
        public async ValueTask PublishAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            // before: a dead letter is in its queue when its publish is counted
            publishes.Add();
            await inner.PublishAsync(envelope, cancellationToken);
        }
    }

    /// <summary>the time of the system, plus what the scenario adds to it.</summary>
    private sealed class Clock : TimeProvider
    {
        private long _aheadByTicks;

        public void Advance(TimeSpan by) => Interlocked.Add(ref _aheadByTicks, by.Ticks);

        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + TimeSpan.FromTicks(Interlocked.Read(ref _aheadByTicks));
    }
}
