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
    /// window, checks that nothing is left in the outbox or was dead-lettered, and stops the hosts.
    /// </summary>
    /// <param name="scenario">publishes the messages and waits for what it expects to happen.
    /// Its token is cancelled when the scenario takes too long.</param>
    /// <exception cref="TimeoutException">the scenario did not finish in time.</exception>
    protected async Task RunScenarioAsync(
        string combination,
        int hostsCount,
        HostSetup setup,
        Func<ScenarioRun, CancellationToken, Task> scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        // when anything below fails, disposing the run stops the hosts and what it has to say about it is only logged:
        // what failed is what the test reports
        await using var run = CreateRun(combination);

        await run.StartHostsAsync(hostsCount, setup);

        await RunWithTimeoutAsync(token => scenario(run, token));

        await Task.Delay(ObservationWindow);
        await AssertNothingIsLeftBehindAsync(run);

        await run.StopHostsAsync();
    }

    /// <summary>
    /// every message was relayed, and none was given up on.
    /// </summary>
    protected static async Task AssertNothingIsLeftBehindAsync(ScenarioRun run)
    {
        using var timeout = new CancellationTokenSource(ChecksTimeout);

        Assert.Equal(0, await run.CountOutboxMessagesAsync(timeout.Token));

        if (run.SupportsDeadLetters)
            Assert.Equal(0, await run.CountDeadLettersAsync(timeout.Token));
    }

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
