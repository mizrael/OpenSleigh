using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas;
using OpenSleigh.Outbox;
using OpenSleigh.Transport;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// the only host of a service is stopped between two steps of a saga, and another one is started: what a deployment
/// does to a saga that is in flight. The saga goes on from what is stored, nothing of it lived in the first host.
/// </summary>
public class HostRestartScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private const string Customer = "Ada Lovelace";
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(50);

    private static readonly Type[] HandledBeforeTheRestart = [typeof(StartOnboarding)];
    private static readonly Type[] HandledAfterTheRestart = [typeof(VerifyIdentity), typeof(ActivateAccount)];

    /// <summary>
    /// the first host stores the first step and stops before it relays the message that starts the second:
    /// the saga state and the outbox are all there is. What reads them is code of the store:
    /// every store runs it, and one combination has Kafka.
    /// </summary>
    [Theory]
    [MatrixData(
        Only = [Combinations.SqlServerRabbitMq, Combinations.PostgreSqlRabbitMq, Combinations.MongoDbRabbitMq, Combinations.PostgreSqlKafka],
        HostsCounts = [1])]
    public Task a_new_host_goes_on_from_a_message_left_in_the_outbox(string combination, int hostsCount)
    {
        var relay = new PausableRelay();

        return RunRestartAsync(
            combination,
            hostsCount,
            configureFirstHost: services => services.Intercept(_ => relay),
            startAsync: async (run, handledByFirstHost, publishAsync, cancellationToken) =>
            {
                // the relay is paused while the first step is being handled: the message it publishes is stored after that
                var firstStep = handledByFirstHost.HoldNext(_ => true);
                await publishAsync();
                await run.WaitForAsync("the first step to reach its handler", firstStep.Entered, cancellationToken);
                relay.Pause();
                firstStep.Release();
            },
            waitUntilBetweenStepsAsync: (run, cancellationToken) => WaitForOutboxAsync(run, messagesCount: 1, cancellationToken));
    }

    /// <summary>
    /// the first host relays the message that starts the second step and stops when it gets it back,
    /// before handling it: the message is at the broker, which gives it to the next host.
    /// That is up to the transport: each one runs it twice, over the three stores.
    /// </summary>
    [Theory]
    [MatrixData(
        Only = [Combinations.SqlServerRabbitMq, Combinations.MongoDbRabbitMq, Combinations.PostgreSqlKafka, Combinations.MongoDbKafka],
        HostsCounts = [1])]
    public Task a_new_host_goes_on_from_a_message_left_at_the_broker(string combination, int hostsCount)
    {
        var secondStepArrived = new Expectation(1);

        return RunRestartAsync(
            combination,
            hostsCount,
            configureFirstHost: services => services.Decorate<IMessageProcessor>(
                (_, inner) => new HoldingProcessor(inner, typeof(VerifyIdentity), secondStepArrived)),
            startAsync: (_, _, publishAsync, _) => publishAsync(),
            waitUntilBetweenStepsAsync: async (run, cancellationToken) =>
            {
                await run.WaitForAsync("the message of the second step to come back to the first host", secondStepArrived.Reached, cancellationToken);
                await WaitForOutboxAsync(run, messagesCount: 0, cancellationToken);
            });
    }

    private async Task RunRestartAsync(
        string combination,
        int hostsCount,
        Action<IServiceCollection> configureFirstHost,
        Func<ScenarioRun, HandlerRuns, Func<Task>, CancellationToken, Task> startAsync,
        Func<ScenarioRun, CancellationToken, Task> waitUntilBetweenStepsAsync)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var handledByFirstHost = new HandlerRuns();
        var handledBySecondHost = new HandlerRuns();

        var firstHostSetup = SetupOf(services =>
        {
            services.RecordHandlerRuns(handledByFirstHost);
            configureFirstHost(services);
        });
        var secondHostSetup = SetupOf(services => services.RecordHandlerRuns(handledBySecondHost));

        await RunScenarioAsync(combination, async (run, cancellationToken) =>
        {
            var firstHosts = await run.StartHostsAsync(hostsCount, firstHostSetup);
            await startAsync(run, handledByFirstHost,
                () => firstHosts[0].PublishNewAsync(new StartOnboarding(correlationId, Customer), cancellationToken),
                cancellationToken);

            await run.WaitForReleasedSagaAsync<OnboardingSaga>(correlationId, HandledBeforeTheRestart.Length, cancellationToken);
            await waitUntilBetweenStepsAsync(run, cancellationToken);

            // nobody is left running: what goes on after this starts from the store and the broker
            foreach (var host in firstHosts)
                await host.StopAsync();
            await run.StartHostsAsync(hostsCount, secondHostSetup);

            var stored = await run.WaitForReleasedSagaAsync<OnboardingSaga>(
                correlationId, HandledBeforeTheRestart.Length + HandledAfterTheRestart.Length, cancellationToken);
            Assert.True(stored.IsCompleted);

            var state = Assert.IsType<OnboardingState>(stored.StateData);
            Assert.Equal(Customer, state.Customer);
            Assert.Equal(HandledBeforeTheRestart.Concat(HandledAfterTheRestart).Select(step => step.Name), state.Steps);
        });

        // also after the observation window: each host handled its steps, once
        Assert.Equal(HandledBeforeTheRestart, handledByFirstHost.Runs.Select(handlerRun => handlerRun.MessageType));
        Assert.Equal(HandledAfterTheRestart, handledBySecondHost.Runs.Select(handlerRun => handlerRun.MessageType));
    }

    private static HostSetup SetupOf(Action<IServiceCollection> configureServices)
        => new(bus => bus.AddSaga<OnboardingSaga, OnboardingState>())
        {
            ConfigureServices = configureServices
        };

    // the relay removes a message after publishing it: the one that started the saga may still be there for a moment
    private static Task WaitForOutboxAsync(ScenarioRun run, long messagesCount, CancellationToken cancellationToken)
    {
        async Task PollAsync()
        {
            while (await run.CountOutboxMessagesAsync(cancellationToken) != messagesCount)
                await Task.Delay(PollingInterval, cancellationToken);
        }

        return run.WaitForAsync($"the outbox to hold {messagesCount} message(s)", PollAsync(), cancellationToken);
    }

    /// <summary>
    /// keeps the messages of one type from being processed for as long as its host runs: the host is stopped
    /// while it holds one, and leaves it to be delivered again. It holds the message before its saga is locked:
    /// <see cref="HandlerRuns.HoldNext"/> holds it after, and the host would stop in the middle of a step.
    /// </summary>
    private sealed class HoldingProcessor(IMessageProcessor inner, Type heldMessageType, Expectation arrived) : IMessageProcessor
    {
        public async ValueTask ProcessAsync(MessageEnvelope outboxMessage, CancellationToken cancellationToken = default)
        {
            if (outboxMessage.MessageType == heldMessageType)
            {
                arrived.Signal();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            await inner.ProcessAsync(outboxMessage, cancellationToken);
        }
    }
}
