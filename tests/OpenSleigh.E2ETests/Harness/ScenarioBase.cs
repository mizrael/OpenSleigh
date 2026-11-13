using System.ComponentModel;
using System.Diagnostics;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Harness;

/// <summary>
/// the base of every scenario class. A scenario is a theory with <see cref="MatrixDataAttribute"/>
/// that calls <see cref="RunScenarioAsync"/>, or drives a <see cref="ScenarioRun"/> of its own
/// from <see cref="CreateRun"/> when it needs hosts with different roles or lifetimes.
/// </summary>
[Category(E2ECategory)]
[Trait("Category", E2ECategory)]
public abstract class ScenarioBase
{
    public const string E2ECategory = "E2E";

    // an upper bound, spent only by a scenario that fails: one that works ends as soon as what it expects has happened
    private static readonly TimeSpan ScenarioTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ChecksTimeout = TimeSpan.FromSeconds(30);

    // how long the hosts are left running once the scenario is over, for a message that should not
    // have been delivered, or delivered again, to show up. It spans several outbox passes and transport retries
    private static readonly TimeSpan ObservationWindow = TimeSpan.FromSeconds(3);

    protected readonly ITestOutputHelper Console;

    protected ScenarioBase(ITestOutputHelper console)
    {
        Console = console ?? throw new ArgumentNullException(nameof(console));
    }

    /// <summary>
    /// a run with no hosts yet, for a scenario that starts and stops them itself. Dispose it.
    /// </summary>
    protected ScenarioRun CreateRun(string combination)
        => ScenarioRun.Create(Combinations.Get(combination), Console);

    /// <summary>
    /// starts identical hosts on a combination, runs the scenario, keeps the hosts running for the observation
    /// window, checks that nothing is left in the outbox or was dead-lettered (or what <paramref name="verify"/> says),
    /// and stops the hosts.
    /// </summary>
    /// <param name="scenario">publishes the messages and waits for what it expects to happen.
    /// Its token is cancelled when the scenario takes too long.</param>
    /// <param name="verify">what must hold once the observation window is over, while the hosts still run.
    /// <see cref="AssertNothingIsLeftBehindAsync"/> when not given: a scenario that expects dead letters says which.</param>
    /// <exception cref="TimeoutException">the scenario did not finish in time.</exception>
    protected async Task RunScenarioAsync(
        string combination,
        int hostsCount,
        HostSetup setup,
        Func<ScenarioRun, CancellationToken, Task> scenario,
        Func<ScenarioRun, Task>? verify = null)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        // when anything below fails, disposing the run stops the hosts and what it has to say about it is only logged:
        // what failed is what the test reports
        await using var run = CreateRun(combination);

        await run.StartHostsAsync(hostsCount, setup);

        await RunWithTimeoutAsync(token => scenario(run, token));

        await Task.Delay(ObservationWindow);
        await (verify ?? AssertNothingIsLeftBehindAsync)(run);

        await run.StopHostsAsync();
    }

    /// <summary>
    /// every message was relayed, and none was given up on.
    /// </summary>
    protected static async Task AssertNothingIsLeftBehindAsync(ScenarioRun run)
    {
        await AssertOutboxIsEmptyAsync(run);

        if (!run.SupportsDeadLetters)
            return;

        using var timeout = new CancellationTokenSource(ChecksTimeout);
        Assert.Empty(await run.ReadDeadLettersAsync(timeout.Token));
    }

    /// <summary>
    /// every message was relayed.
    /// </summary>
    protected static async Task AssertOutboxIsEmptyAsync(ScenarioRun run)
    {
        using var timeout = new CancellationTokenSource(ChecksTimeout);
        Assert.Equal(0, await run.CountOutboxMessagesAsync(timeout.Token));
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

    /// <summary>
    /// leaves the hosts running for a message that should not be delivered, or delivered again, to show up.
    /// </summary>
    protected static Task ObserveAsync() => Task.Delay(ObservationWindow);

    /// <exception cref="TimeoutException">the scenario did not finish in time.</exception>
    protected static async Task RunWithTimeoutAsync(Func<CancellationToken, Task> scenario)
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
