using System.ComponentModel;
using System.Diagnostics;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Harness;

/// <summary>
/// the base of every scenario class. A scenario is a theory with <see cref="MatrixDataAttribute"/>
/// that calls one of the <c>RunScenarioAsync</c> methods: both end with the same checks.
/// </summary>
[Category(E2ECategory)]
[Trait("Category", E2ECategory)]
public abstract class ScenarioBase
{
    public const string E2ECategory = "E2E";

    // an upper bound, spent only by a scenario that fails: one that works ends as soon as what it expects has happened
    private static readonly TimeSpan ScenarioTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ChecksTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ChecksPollingInterval = TimeSpan.FromMilliseconds(50);

    // how long the hosts are left running once the scenario is over, for a message that should not
    // have been delivered, or delivered again, to show up. It spans several outbox passes and transport retries
    private static readonly TimeSpan ObservationWindow = TimeSpan.FromSeconds(3);

    protected readonly ITestOutputHelper Console;

    protected ScenarioBase(ITestOutputHelper console)
    {
        Console = console ?? throw new ArgumentNullException(nameof(console));
    }

    /// <summary>
    /// runs a scenario that starts its own hosts, with whatever roles and lifetimes it needs, and ends it the way
    /// every scenario ends: the hosts are left running for the observation window, the outbox must be empty,
    /// the dead letters must be the expected ones, and the hosts must stop cleanly.
    /// </summary>
    /// <param name="scenario">starts the hosts, publishes the messages and waits for what it expects to happen.
    /// Its token is cancelled when the scenario takes too long. A host must be running when it returns.</param>
    /// <param name="assertDeadLetters">what the scenario expects to find dead-lettered.
    /// <see cref="AssertNoDeadLettersAsync"/> when not given.</param>
    /// <param name="prepareStore">false for a scenario about hosts that start on a database nobody prepared.</param>
    /// <exception cref="TimeoutException">the scenario did not finish in time.</exception>
    protected async Task RunScenarioAsync(
        string combination,
        Func<ScenarioRun, CancellationToken, Task> scenario,
        Func<ScenarioRun, Task>? assertDeadLetters = null,
        bool prepareStore = true)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        // when anything below fails, disposing the run stops the hosts and cleans up, and what that has to say
        // is only written to the output: what failed is what the test reports
        await using var run = ScenarioRun.Create(Combinations.Get(combination), Console, prepareStore);

        await RunWithTimeoutAsync(token => scenario(run, token));

        await Task.Delay(ObservationWindow);
        await AssertOutboxIsEmptyAsync(run);
        await (assertDeadLetters ?? AssertNoDeadLettersAsync)(run);

        await run.StopHostsAsync();
    }

    /// <summary>
    /// the same, for a scenario whose hosts are identical and all start before it does.
    /// </summary>
    protected Task RunScenarioAsync(
        string combination,
        int hostsCount,
        HostSetup setup,
        Func<ScenarioRun, CancellationToken, Task> scenario,
        Func<ScenarioRun, Task>? assertDeadLetters = null)
        => RunScenarioAsync(
            combination,
            async (run, cancellationToken) =>
            {
                await run.StartHostsAsync(hostsCount, setup);
                await scenario(run, cancellationToken);
            },
            assertDeadLetters);

    // the relay publishes a message before it removes it from the outbox: a message that was handled
    // can still be in there for a moment
    private static async Task AssertOutboxIsEmptyAsync(ScenarioRun run)
    {
        var waited = Stopwatch.StartNew();

        long left;
        do
        {
            using var timeout = new CancellationTokenSource(ChecksTimeout);
            left = await run.CountOutboxMessagesAsync(timeout.Token);
            if (left > 0)
                await Task.Delay(ChecksPollingInterval);
        } while (left > 0 && waited.Elapsed < ChecksTimeout);

        Assert.Equal(0, left);
    }

    /// <summary>
    /// no message was given up on.
    /// </summary>
    protected static async Task AssertNoDeadLettersAsync(ScenarioRun run)
    {
        if (!run.SupportsDeadLetters)
            return;

        using var timeout = new CancellationTokenSource(ChecksTimeout);
        Assert.Empty(await run.ReadDeadLettersAsync(timeout.Token));
    }

    /// <summary>
    /// the only dead letters are messages that reached a host before their saga was stored, which is by design
    /// (see <see cref="ProcessingFailure.IsSagaNotFound"/>): one dead letter for each such failure,
    /// of the message types the scenario says can arrive early, and nothing else.
    /// </summary>
    protected static async Task AssertOnlySagaNotFoundDeadLettersAsync(ScenarioRun run, params Type[] canArriveEarly)
    {
        var notFound = run.ProcessingFailures.Where(failure => failure.IsSagaNotFound).ToArray();
        Assert.All(notFound, failure => Assert.Contains(failure.MessageType, canArriveEarly));

        if (!run.SupportsDeadLetters)
            return;

        var expected = notFound.GroupBy(failure => failure.MessageType)
            .ToDictionary(failures => failures.Key, failures => failures.LongCount());

        using var timeout = new CancellationTokenSource(ChecksTimeout);
        var deadLetters = await run.ReadDeadLettersAsync(timeout.Token);

        Assert.Equal(
            expected.OrderBy(entry => entry.Key.FullName),
            deadLetters.OrderBy(entry => entry.Key.FullName));
    }

    private static async Task RunWithTimeoutAsync(Func<CancellationToken, Task> scenario)
    {
        using var timeout = new CancellationTokenSource(Debugger.IsAttached ? Timeout.InfiniteTimeSpan : ScenarioTimeout);
        try
        {
            await scenario(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"the scenario did not complete within {ScenarioTimeout.TotalSeconds} seconds: something it expected did not happen.");
        }
    }
}
