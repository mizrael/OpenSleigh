using OpenSleigh.Outbox;
using OpenSleigh.Queries;
using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Harness;

/// <summary>
/// waits that say what they were waiting for when the scenario runs out of time, and that give up at once
/// when a host could not process a message: what the scenario expects is then not going to happen.
/// </summary>
public static class ScenarioRunExtensions
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(20);

    /// <summary>how many times a host found a saga locked by someone else, so far.</summary>
    public static int CountLockConflicts(this ScenarioRun run)
        => run.ProcessingFailures.Count(failure => failure.IsLockConflict);

    /// <param name="what">what the scenario waits for, to complete "waiting for ...".</param>
    /// <param name="isExpectedFailure">the failures the scenario expects, besides lock conflicts, which are retried.</param>
    /// <exception cref="TimeoutException">the scenario ran out of time.</exception>
    public static async Task WaitUntilAsync(
        this ScenarioRun run,
        string what,
        Func<bool> isReached,
        CancellationToken cancellationToken,
        Func<ProcessingFailure, bool>? isExpectedFailure = null)
    {
        ArgumentNullException.ThrowIfNull(isReached);

        while (!isReached())
        {
            var unexpected = run.ProcessingFailures.FirstOrDefault(failure
                => !failure.IsLockConflict && isExpectedFailure?.Invoke(failure) != true);
            if (unexpected is not null)
                Assert.Fail($"gave up waiting for {what}: a host could not process a message. {unexpected}");

            try
            {
                await Task.Delay(PollingInterval, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"the scenario ran out of time waiting for {what}.");
            }
        }
    }

    public static async Task WaitForAsync(
        this ScenarioRun run,
        string what,
        Task expected,
        CancellationToken cancellationToken,
        Func<ProcessingFailure, bool>? isExpectedFailure = null)
    {
        ArgumentNullException.ThrowIfNull(expected);

        await run.WaitUntilAsync(what, () => expected.IsCompleted, cancellationToken, isExpectedFailure);
        await expected;
    }

    /// <summary>
    /// the same, for something the scenario does until it is done, such as polling: it is stopped when the wait
    /// ends, however it ends, so that nothing goes on behind a scenario that gave up.
    /// </summary>
    public static async Task<TResult> WaitForAsync<TResult>(
        this ScenarioRun run,
        string what,
        Func<CancellationToken, Task<TResult>> expected,
        CancellationToken cancellationToken,
        Func<ProcessingFailure, bool>? isExpectedFailure = null)
    {
        ArgumentNullException.ThrowIfNull(expected);

        using var working = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var work = expected(working.Token);

        try
        {
            await run.WaitForAsync(what, work, cancellationToken, isExpectedFailure);
            return await work;
        }
        finally
        {
            await working.CancelAsync();
        }
    }

    public static Task WaitForAsync(
        this ScenarioRun run,
        string what,
        Func<CancellationToken, Task> expected,
        CancellationToken cancellationToken,
        Func<ProcessingFailure, bool>? isExpectedFailure = null)
    {
        ArgumentNullException.ThrowIfNull(expected);

        return run.WaitForAsync(what, async token =>
        {
            await expected(token);
            return true;
        }, cancellationToken, isExpectedFailure);
    }

    /// <summary>reads a saga from the store until it is stored with that many messages handled, and released.</summary>
    public static async Task<SagaInstanceInfo> WaitForReleasedSagaAsync<TSaga>(
        this ScenarioRun run,
        string correlationId,
        int handledMessages,
        CancellationToken cancellationToken,
        Func<ProcessingFailure, bool>? isExpectedFailure = null)
    {
        // the reading stops with this wait, however it ends
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stored = run.WaitForSagaAsync<TSaga>(correlationId,
            saga => saga.ProcessedMessages.Count == handledMessages && !saga.IsLocked,
            reading.Token);

        try
        {
            await run.WaitForAsync(
                $"{typeof(TSaga).Name} '{correlationId}' to be stored with {handledMessages} message(s) handled, and released",
                stored, cancellationToken, isExpectedFailure);
            return await stored;
        }
        finally
        {
            await reading.CancelAsync();
        }
    }

    /// <summary>publishes a message that is not in the outbox yet.</summary>
    public static async Task PublishNewAsync(this ScenarioHost host, IMessage message, CancellationToken cancellationToken)
        => Assert.Equal(OutboxAppendResult.Success, await host.PublishAsync(message, cancellationToken));

    /// <summary>
    /// makes messages for one saga meet, instead of hoping that they do: the first of them to get the saga keeps it,
    /// before its handler runs, until another one has found the saga locked.
    /// </summary>
    /// <param name="contends">the handler runs of the messages that are about to be published.</param>
    /// <param name="publish">publishes them. More than one, and enough of them for another host to be given one
    /// while the first is kept.</param>
    public static async Task ForceLockConflictAsync(
        this ScenarioRun run,
        HandlerRuns runs,
        Func<HandlerRun, bool> contends,
        Func<Task> publish,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(publish);

        var conflictsBefore = run.CountLockConflicts();
        var first = runs.HoldNext(contends);
        try
        {
            await publish();

            await run.WaitForAsync("one of the messages that contend for a saga to get it", first.Entered, cancellationToken);
            await run.WaitUntilAsync("another message to find the saga locked by the first",
                () => run.CountLockConflicts() > conflictsBefore, cancellationToken);
        }
        finally
        {
            first.Release();
        }
    }
}
