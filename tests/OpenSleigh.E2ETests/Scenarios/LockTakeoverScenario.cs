using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas;
using OpenSleigh.Outbox;
using OpenSleigh.Persistence.Mongo;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Transport;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// the owner of a saga stops between its handler and its commit, for longer than its lock lasts: a process that
/// hangs, or one that died and whose message is delivered again. The lock is honoured for as long as it lasts,
/// then someone else takes the saga over and finishes it. When the first owner goes on, its commit is refused.
/// </summary>
public class LockTakeoverScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    // a minute by default. Here, long enough for the message to be delivered again while the lock still holds:
    // that takes a few milliseconds, and the scenario fails if it took longer
    private static readonly TimeSpan LockMaxDuration = TimeSpan.FromSeconds(3);

    private const int Messages = 2;

    // not in memory, where a lock never expires. The expiry of a lock is the store's and the retry of a message
    // that finds it is the transport's, so every store runs with one transport and one store with the other.
    // A second host is there to take the saga over: more would only be more hosts that could
    [Theory]
    [MatrixData(
        Only = [Combinations.SqlServerRabbitMq, Combinations.PostgreSqlRabbitMq, Combinations.MongoDbRabbitMq, Combinations.PostgreSqlKafka],
        HostsCounts = [2])]
    public async Task a_saga_whose_owner_stalled_is_taken_over_when_its_lock_expires_and_the_first_owner_is_refused(string combination, int hostsCount)
    {
        var correlationId = Guid.NewGuid().ToString();
        var runs = new HandlerRuns();
        var stall = new Stall();

        var setup = new HostSetup(bus => bus.AddSaga<PayoutSaga, PayoutState>())
        {
            ConfigureServices = services => services
                .RecordHandlerRuns(runs)
                .AddSingleton(new SqlSagaStateRepositoryOptions(LockMaxDuration))
                .AddSingleton(new MongoSagaStateRepositoryOptions(LockMaxDuration))
                .Intercept(_ => new StallsBeforeTheFirstCommit(stall))
        };

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                // the first owner is given the message the way a transport gives it, and not by a transport:
                // the consumer of a host that stalls is stuck with it, and would keep what else it was given
                var owner = run.Hosts[0];
                var start = MessageEnvelope.Create(new StartPayout(correlationId), owner.Services.GetRequiredService<ISystemInfo>());

                await using var ownerScope = owner.Services.CreateAsyncScope();
                var processor = ownerScope.ServiceProvider.GetRequiredService<IMessageProcessor>();
                var firstOwner = Task.Run(() => processor.ProcessAsync(start, cancellationToken).AsTask(), cancellationToken);

                await run.WaitUntilAsync("the first owner to stall before its commit",
                    () => stall.Reached.IsCompleted || firstOwner.IsCompleted, cancellationToken);
                Assert.False(firstOwner.IsCompleted, "the first owner did not stall.");

                // the handler ran, and nothing it did is stored
                var locked = await run.FindSagaAsync<PayoutSaga>(correlationId, cancellationToken);
                Assert.NotNull(locked);
                Assert.True(locked.IsLocked);
                Assert.Empty(locked.ProcessedMessages);

                // the message is delivered again, by the transport this time, and finds the lock
                await run.Hosts[^1].Services.GetRequiredService<IPublisher>().PublishAsync(start, cancellationToken);
                await run.WaitUntilAsync("the message, delivered again, to find the saga locked",
                    () => run.CountLockConflicts() > 0, cancellationToken);

                var takenOver = await run.WaitForReleasedSagaAsync<PayoutSaga>(correlationId, Messages, cancellationToken);
                Assert.True(takenOver.IsCompleted);

                // the first owner goes on with the commit it was about to make, with the lock it had
                stall.Resume();
                var refusal = await Assert.ThrowsAnyAsync<Exception>(() => firstOwner);
                Assert.True(refusal.IsLockConflict(), refusal.ToString());

                var afterRefusal = await run.FindSagaAsync<PayoutSaga>(correlationId, cancellationToken);
                Assert.NotNull(afterRefusal);
                Assert.True(afterRefusal.IsCompleted);
                Assert.False(afterRefusal.IsLocked);
                Assert.Equal(Messages, afterRefusal.ProcessedMessages.Count);
                Assert.Equal(1, Assert.IsType<PayoutState>(afterRefusal.StateData).Executions);
            },
            // whoever is given the message while the lock lasts finds the saga locked, and so does the first owner
            // in the end: nothing else goes wrong. What the first owner published is not in the outbox
            AssertOnlyLockConflictsAsync);

        // delivery is at least once: the handler ran for the first owner, and again for the one that took over.
        // Only the second run is stored, and only its message was published
        var executions = runs.RunsOf<StartPayout>();
        Assert.Equal(2, executions.Count);
        Assert.Single(executions.Select(run => run.InstanceId).Distinct());
        Assert.Single(runs.RunsOf<PayoutExecuted>());

        // the lock was written after this timestamp was taken: nobody had the saga before it expired
        var takenOverAfter = Stopwatch.GetElapsedTime(stall.LockRequestedAt, executions[1].At);
        Assert.True(takenOverAfter >= LockMaxDuration,
            $"the saga was taken over {takenOverAfter.TotalMilliseconds} ms after it was locked: its lock lasts {LockMaxDuration.TotalMilliseconds} ms.");
    }

    /// <summary>the one stall the scenario causes, and ends. One for all the hosts.</summary>
    private sealed class Stall
    {
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _resumed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _claimed;
        private long _lockRequestedAt;

        public Task Reached => _reached.Task;

        /// <summary>when the first lock of the scenario was asked for: before it was written.</summary>
        public long LockRequestedAt => Interlocked.Read(ref _lockRequestedAt);

        public void LockRequested()
            => Interlocked.CompareExchange(ref _lockRequestedAt, Stopwatch.GetTimestamp(), 0);

        /// <returns>what to wait for before going on, for the first commit of the scenario. Null for the others.</returns>
        public Task? TryStall(CancellationToken cancellationToken)
        {
            if (Interlocked.CompareExchange(ref _claimed, 1, 0) != 0)
                return null;

            _reached.TrySetResult();
            return _resumed.Task.WaitAsync(cancellationToken);
        }

        public void Resume() => _resumed.TrySetResult();
    }

    /// <summary>
    /// the first commit of the scenario waits until the scenario lets it go on, then goes to the store as it is.
    /// </summary>
    private sealed class StallsBeforeTheFirstCommit(Stall stall) : SagaStateRepositoryInterceptor
    {
        public override ValueTask<string> LockAsync(ISagaStateRepository repository, ISagaInstance state, CancellationToken cancellationToken)
        {
            stall.LockRequested();
            return base.LockAsync(repository, state, cancellationToken);
        }

        public override async ValueTask CommitAsync(ISagaStateCommitter repository, ISagaInstance state, CancellationToken cancellationToken)
        {
            if (stall.TryStall(cancellationToken) is { } resumed)
                await resumed;

            await base.CommitAsync(repository, state, cancellationToken);
        }
    }
}
