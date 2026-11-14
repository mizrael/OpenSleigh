using System.Collections.Concurrent;
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

    private static readonly string[] HandledBeforeTheRestart = [nameof(StartOnboarding)];
    private static readonly string[] HandledAfterTheRestart = [nameof(VerifyIdentity), nameof(ActivateAccount)];

    /// <summary>
    /// the first host stores the first step and stops before it relays the message that starts the second:
    /// the saga state and the outbox are all there is.
    /// </summary>
    [Theory]
    [MatrixData(RequiresSharedInfrastructure = true, HostsCounts = [1])]
    public Task a_new_host_goes_on_from_a_message_left_in_the_outbox(string combination, int hostsCount)
    {
        // paused by the first step, while it is handled: the message it publishes is stored after that
        var relay = new PausableRelay();

        return RunRestartAsync(
            combination,
            hostsCount,
            configureFirstHost: services => services.Intercept(_ => relay),
            onStepHandledByFirstHost: relay.Pause,
            waitUntilBetweenStepsAsync: (run, cancellationToken) => WaitForOutboxAsync(run, messagesCount: 1, cancellationToken));
    }

    /// <summary>
    /// the first host relays the message that starts the second step and stops when it gets it back,
    /// before handling it: the message is at the broker, which gives it to the next host.
    /// </summary>
    [Theory]
    [MatrixData(RequiresSharedInfrastructure = true, HostsCounts = [1])]
    public Task a_new_host_goes_on_from_a_message_left_at_the_broker(string combination, int hostsCount)
    {
        var secondStepArrived = new Expectation(1);

        return RunRestartAsync(
            combination,
            hostsCount,
            configureFirstHost: services => services.Decorate<IMessageProcessor>(
                (_, inner) => new HoldingProcessor(inner, typeof(VerifyIdentity), secondStepArrived)),
            onStepHandledByFirstHost: () => { },
            waitUntilBetweenStepsAsync: async (run, cancellationToken) =>
            {
                await secondStepArrived.Reached.WaitAsync(cancellationToken);
                await WaitForOutboxAsync(run, messagesCount: 0, cancellationToken);
            });
    }

    private async Task RunRestartAsync(
        string combination,
        int hostsCount,
        Action<IServiceCollection> configureFirstHost,
        Action onStepHandledByFirstHost,
        Func<ScenarioRun, CancellationToken, Task> waitUntilBetweenStepsAsync)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var handledByFirstHost = new ConcurrentQueue<string>();
        var handledBySecondHost = new ConcurrentQueue<string>();
        var completed = new Expectation(HandledAfterTheRestart.Length);

        var firstHostSetup = SetupOf(
            step =>
            {
                handledByFirstHost.Enqueue(step);
                onStepHandledByFirstHost();
            },
            configureFirstHost);
        var secondHostSetup = SetupOf(
            step =>
            {
                handledBySecondHost.Enqueue(step);
                completed.Signal();
            });

        await RunScenarioAsync(combination, async (run, cancellationToken) =>
        {
            var firstHosts = await run.StartHostsAsync(hostsCount, firstHostSetup);
            await firstHosts[0].PublishAsync(new StartOnboarding(correlationId, Customer), cancellationToken);

            await run.WaitForSagaAsync<OnboardingSaga>(correlationId,
                saga => saga.ProcessedMessages.Count == HandledBeforeTheRestart.Length && !saga.IsLocked, cancellationToken);
            await waitUntilBetweenStepsAsync(run, cancellationToken);

            // nobody is left running: what goes on after this starts from the store and the broker
            foreach (var host in firstHosts)
                await host.StopAsync();
            await run.StartHostsAsync(hostsCount, secondHostSetup);

            await completed.Reached.WaitAsync(cancellationToken);

            // the handler reports before its saga is stored
            var stored = await run.WaitForSagaAsync<OnboardingSaga>(correlationId,
                saga => saga.IsCompleted && !saga.IsLocked, cancellationToken);

            var state = Assert.IsType<OnboardingState>(stored.StateData);
            Assert.Equal(Customer, state.Customer);
            Assert.Equal(HandledBeforeTheRestart.Concat(HandledAfterTheRestart), state.Steps);
            Assert.Equal(state.Steps.Count, stored.ProcessedMessages.Count);
        });

        Assert.Equal(HandledBeforeTheRestart, handledByFirstHost);
        Assert.Equal(HandledAfterTheRestart, handledBySecondHost);
    }

    private static HostSetup SetupOf(OnboardingStepHandled onStepHandled, Action<IServiceCollection>? configureServices = null)
        => new(bus => bus.AddSaga<OnboardingSaga, OnboardingState>())
        {
            ConfigureServices = services =>
            {
                services.AddSingleton(onStepHandled);
                configureServices?.Invoke(services);
            }
        };

    // the relay removes a message after publishing it: the one that started the saga may still be there for a moment
    private static async Task WaitForOutboxAsync(ScenarioRun run, long messagesCount, CancellationToken cancellationToken)
    {
        while (await run.CountOutboxMessagesAsync(cancellationToken) != messagesCount)
            await Task.Delay(PollingInterval, cancellationToken);
    }

    /// <summary>
    /// keeps the messages of one type from being processed for as long as its host runs: the host is stopped
    /// while it handles one, and leaves it to be delivered again.
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
