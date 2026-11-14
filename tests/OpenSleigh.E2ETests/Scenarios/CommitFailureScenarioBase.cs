using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas;
using OpenSleigh.Queries;
using OpenSleigh.Transport;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// a handler does its work and its saga cannot be committed, as when the database goes away at that moment.
/// The scenarios that share this story each look at one side of what is left behind.
/// </summary>
public abstract class CommitFailureScenarioBase(ITestOutputHelper console) : ScenarioBase(console)
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(20);

    /// <param name="PackingAttemptsAfterFailedCommit">what the stored state says once the commit has failed.</param>
    /// <param name="ProcessedMessagesAfterFailedCommit">how many messages the stored saga has handled by then.</param>
    protected sealed record Outcome(
        int PackingAttemptsAfterFailedCommit,
        int ProcessedMessagesAfterFailedCommit,
        int PackingAttemptsAtTheEnd,
        int TimesPackingWasHandled,
        int TimesPackedWasHandled,
        IReadOnlyCollection<ProcessingFailure> Failures);

    /// <summary>
    /// a shipment is asked to pack its parcel and the commit of that fails, once. Nothing retries a message
    /// that failed for another reason than a lock: it is dead-lettered, and the shipment is asked again,
    /// as whoever looks after the dead letters would.
    /// </summary>
    protected async Task<Outcome> PackAParcelWithACommitThatFailsOnceAsync(string combination, int hostsCount)
    {
        var correlationId = Guid.NewGuid().ToString();
        var commitFails = new Fault("the saga cannot be committed, on purpose.");
        var packing = new Expectation(2);
        var packed = new Expectation(1);
        Action<IMessageContext<PackParcel>> onPacking = _ => packing.Signal();
        Action<IMessageContext<ParcelPacked>> onPacked = _ => packed.Signal();

        var setup = new HostSetup(bus => bus.AddSaga<ShipmentSaga, ShipmentState>())
        {
            ConfigureServices = services => services
                .AddSingleton(onPacking)
                .AddSingleton(onPacked)
                .FailCommitsThatPublish<ParcelPacked>(commitFails)
        };

        SagaInstanceInfo? afterFailedCommit = null;
        var packingAttemptsAfterFailedCommit = 0;
        var packingAttemptsAtTheEnd = 0;
        IReadOnlyCollection<ProcessingFailure> failures = [];

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                await run.AnyHost.PublishAsync(new StartShipment(correlationId), cancellationToken);
                await WaitForShipmentAsync(run, stored => stored.ProcessedMessages.Count == 1 && !stored.IsLocked);

                commitFails.StartFor(failures: 1);
                await run.AnyHost.PublishAsync(new PackParcel(correlationId), cancellationToken);

                // the host has given the message back to its transport: it is done with it
                await commitFails.WaitForHitsAsync(1, cancellationToken);
                while (run.ProcessingFailures.Count == 0)
                    await Task.Delay(PollingInterval, cancellationToken);

                // a saga left locked would stay so until its lock expires, long after this scenario has given up
                afterFailedCommit = await WaitForShipmentAsync(run, stored => !stored.IsLocked);
                packingAttemptsAfterFailedCommit = PackingAttemptsOf(afterFailedCommit);

                await run.AnyHost.PublishAsync(new PackParcel(correlationId), cancellationToken);
                await packed.Reached.WaitAsync(cancellationToken);

                var completed = await WaitForShipmentAsync(run, stored => stored.IsCompleted);
                packingAttemptsAtTheEnd = PackingAttemptsOf(completed);

                Task<SagaInstanceInfo> WaitForShipmentAsync(ScenarioRun scenarioRun, Func<SagaInstanceInfo, bool> isAsExpected)
                    => scenarioRun.WaitForSagaAsync<ShipmentSaga>(correlationId, isAsExpected, cancellationToken);
            },
            async run =>
            {
                failures = run.ProcessingFailures.ToArray();

                // the message whose commit failed, and nothing else
                if (run.SupportsDeadLetters)
                    Assert.Equal(
                        new Dictionary<Type, long> { [typeof(PackParcel)] = 1 },
                        await run.ReadDeadLettersAsync(CancellationToken.None));
            });

        return new Outcome(
            packingAttemptsAfterFailedCommit,
            afterFailedCommit!.ProcessedMessages.Count,
            packingAttemptsAtTheEnd,
            packing.Count,
            packed.Count,
            failures);
    }

    // read at once: in memory the state that is returned is the one the saga goes on changing
    private static int PackingAttemptsOf(SagaInstanceInfo shipment)
        => Assert.IsType<ShipmentState>(shipment.StateData).PackingAttempts;
}
