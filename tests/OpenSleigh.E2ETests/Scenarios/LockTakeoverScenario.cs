using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas;
using OpenSleigh.Persistence.Mongo;
using OpenSleigh.Persistence.SQL;
using OpenSleigh.Transport;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// a host dies after its handler ran and before the saga is stored: the saga stays locked by a host that is gone,
/// and its message comes back. The lock is honoured for as long as it lasts, then someone else takes the saga over
/// and finishes it. Whatever the first owner still tries with its lock is refused.
/// </summary>
public class LockTakeoverScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    // a minute by default: here, as long as the scenario can afford to wait
    private static readonly TimeSpan LockMaxDuration = TimeSpan.FromSeconds(2);

    private const int Messages = 2;

    // not in memory: a host that dies takes its sagas with it, and there the locks never expire.
    // With one host its replacement takes the saga over, with a pair the host that is left does;
    // more hosts would only be more hosts that are left
    [Theory]
    [MatrixData(RequiresSharedInfrastructure = true, HostsCounts = [1, 2])]
    public async Task a_saga_left_locked_by_a_host_that_died_is_taken_over_when_its_lock_expires(string combination, int hostsCount)
    {
        var correlationId = Guid.NewGuid().ToString();
        var runs = new HandlerRuns(Messages);
        var crash = new Crash();

        var setup = new HostSetup(bus => bus.AddSaga<TransferSaga, TransferState>())
        {
            ConfigureServices = services => services
                .AddSingleton(runs)
                .AddSingleton<HostLife>()
                .AddSingleton(new SqlSagaStateRepositoryOptions(LockMaxDuration))
                .AddSingleton(new MongoSagaStateRepositoryOptions(LockMaxDuration))
                .Intercept(provider => new DiesBeforeItsFirstCommit(crash, provider.GetRequiredService<HostLife>()))
        };

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                await run.AnyHost.PublishAsync(new StartTransfer(correlationId), cancellationToken);

                // the handler ran, and nothing it did is stored: the saga is there, locked, with no message handled
                var dead = await crash.Happened.WaitAsync(cancellationToken);
                var locked = await run.FindSagaAsync<TransferSaga>(correlationId, cancellationToken);
                Assert.NotNull(locked);
                Assert.True(locked.IsLocked);
                Assert.Empty(locked.ProcessedMessages);

                var deadHost = run.Hosts.Single(host => ReferenceEquals(host.Services.GetRequiredService<HostLife>(), dead.Host));
                await deadHost.StopAsync();
                if (hostsCount == 1)
                    await run.StartHostAsync(setup);

                await runs.AllHandled.WaitAsync(cancellationToken);
                var completed = await run.WaitForSagaAsync<TransferSaga>(correlationId,
                    saga => saga.IsCompleted && !saga.IsLocked, cancellationToken);
                Assert.Equal(Messages, completed.ProcessedMessages.Count);

                // the host that died comes back to life with the lock it had: it cannot release the saga, nor change it
                await using var scope = run.AnyHost.Services.CreateAsyncScope();
                var repository = scope.ServiceProvider.GetRequiredService<ISagaStateRepository>();
                var staleOwner = await dead.Owner.WithItsLockAsync(cancellationToken);
                await Assert.ThrowsAsync<LockException>(() => repository.ReleaseAsync(staleOwner, cancellationToken).AsTask());

                var afterStaleRelease = await run.FindSagaAsync<TransferSaga>(correlationId, cancellationToken);
                Assert.NotNull(afterStaleRelease);
                Assert.True(afterStaleRelease.IsCompleted);
                Assert.Equal(Messages, afterStaleRelease.ProcessedMessages.Count);
                Assert.Equal(1, Assert.IsType<TransferState>(afterStaleRelease.StateData).Executions);
            },
            async run =>
            {
                await AssertNoDeadLettersAsync(run);

                // until the lock expires, whoever is given the message finds the saga locked, and nothing else goes wrong
                Assert.All(run.ProcessingFailures, failure => Assert.True(failure.IsLockConflict, failure.ToString()));
            });

        // delivery is at least once: the handler ran on the host that died, and again on the one that took over.
        // Only the second run is stored, and only its message was published
        var executions = runs.RunsOf<StartTransfer>();
        Assert.Equal(2, executions.Count);
        Assert.Single(executions.Select(run => run.InstanceId).Distinct());
        Assert.Single(runs.RunsOf<TransferExecuted>());

        // the lock was written after this timestamp was taken: nobody had the saga before it expired
        var takenOverAfter = Stopwatch.GetElapsedTime(crash.LockRequestedAt, executions[1].At);
        Assert.True(takenOverAfter >= LockMaxDuration,
            $"the saga was taken over {takenOverAfter.TotalMilliseconds} ms after it was locked: its lock lasts {LockMaxDuration.TotalMilliseconds} ms.");
    }

    /// <summary>whether a host is still alive. One per host.</summary>
    private sealed class HostLife
    {
        private int _isOver;

        public bool IsOver => Volatile.Read(ref _isOver) != 0;

        public void End() => Volatile.Write(ref _isOver, 1);
    }

    private sealed record DeadOwner(HostLife Host, SagaOwner Owner);

    /// <summary>what the scenario knows about the one crash it causes. One for all the hosts.</summary>
    private sealed class Crash
    {
        private readonly TaskCompletionSource<DeadOwner> _happened = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _claimed;
        private long _lockRequestedAt;

        public Task<DeadOwner> Happened => _happened.Task;

        /// <summary>when the lock that is left behind was asked for: before it was written.</summary>
        public long LockRequestedAt => Interlocked.Read(ref _lockRequestedAt);

        public void LockRequested()
        {
            if (Volatile.Read(ref _claimed) == 0)
                Interlocked.Exchange(ref _lockRequestedAt, Stopwatch.GetTimestamp());
        }

        public bool TryClaim() => Interlocked.CompareExchange(ref _claimed, 1, 0) == 0;

        public void Report(DeadOwner dead) => _happened.TrySetResult(dead);
    }

    /// <summary>
    /// the first host that is about to store the saga dies instead: its commit never reaches the store, and neither
    /// does anything it tries afterwards. It stays that way until the scenario stops it, as a process that is gone would.
    /// </summary>
    private sealed class DiesBeforeItsFirstCommit(Crash crash, HostLife host) : SagaStateRepositoryInterceptor
    {
        public override ValueTask<string> LockAsync(ISagaStateRepository repository, ISagaInstance state, CancellationToken cancellationToken)
        {
            crash.LockRequested();
            return base.LockAsync(repository, state, cancellationToken);
        }

        public override async ValueTask CommitAsync(ISagaStateCommitter repository, ISagaInstance state, CancellationToken cancellationToken)
        {
            if (!crash.TryClaim())
            {
                await base.CommitAsync(repository, state, cancellationToken);
                return;
            }

            host.End();
            crash.Report(new DeadOwner(host, SagaOwner.Of(state)));

            // until the host is stopped: its message is then left to be delivered again
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public override ValueTask ReleaseAsync(ISagaStateRepository repository, ISagaInstance state, CancellationToken cancellationToken)
            => host.IsOver
                ? throw new InvalidOperationException("this host is dead: it cannot reach the store.")
                : base.ReleaseAsync(repository, state, cancellationToken);
    }

    /// <summary>a saga as one of its owners had it: with the lock that owner was given.</summary>
    private sealed record SagaOwner(string InstanceId, string TriggerMessageId, string CorrelationId, SagaDescriptor Descriptor, string LockId)
    {
        public static SagaOwner Of(ISagaInstance state)
            => new(state.InstanceId, state.TriggerMessageId, state.CorrelationId, state.Descriptor, state.LockId);

        public async Task<ISagaInstance> WithItsLockAsync(CancellationToken cancellationToken)
        {
            var instance = new SagaInstance(InstanceId, TriggerMessageId, CorrelationId, Descriptor);
            await instance.LockAsync(new AlreadyLocked(LockId), cancellationToken);
            return instance;
        }
    }

    /// <summary>gives an instance the lock it once had, without asking the store.</summary>
    private sealed class AlreadyLocked(string lockId) : ISagaStateRepository
    {
        public ValueTask<string> LockAsync(ISagaInstance state, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(lockId);

        public ValueTask<ISagaInstance?> FindAsync<TM>(SagaDescriptor descriptor, IMessageContext<TM> messageContext, CancellationToken cancellationToken = default)
            where TM : IMessage
            => throw new NotSupportedException();

        public ValueTask ReleaseAsync(ISagaInstance state, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
