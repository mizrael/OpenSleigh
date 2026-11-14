using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.E2ETests.Harness.FaultInjection;
using OpenSleigh.E2ETests.Sagas;
using Xunit.Abstractions;

namespace OpenSleigh.E2ETests.Scenarios;

/// <summary>
/// a message that has no saga to go to: its saga was never started, or is already over.
/// Neither runs a handler; the first is dead-lettered, the second is ignored.
/// </summary>
public class MessageWithoutSagaScenario(ITestOutputHelper console) : ScenarioBase(console)
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(20);

    // a pair of hosts shows what five would: whether a message one host gave up on is given to another
    [Theory]
    [MatrixData(HostsCounts = [1, 2])]
    public async Task a_message_for_a_saga_that_was_never_started_is_dead_lettered_once(string combination, int hostsCount)
    {
        var correlationId = Guid.NewGuid().ToString();
        var runs = new HandlerRuns(1);

        var setup = new HostSetup(bus => bus.AddSaga<ReservationSaga>())
        {
            ConfigureServices = services => services.AddSingleton(runs)
        };

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                await run.AnyHost.PublishAsync(new CancelReservation(correlationId), cancellationToken);

                while (run.ProcessingFailures.Count == 0)
                    await Task.Delay(PollingInterval, cancellationToken);
            },
            async run =>
            {
                await AssertOnlySagaNotFoundDeadLettersAsync(run, typeof(CancelReservation));

                // given up on at once: no host is given the message a second time
                var failure = Assert.Single(run.ProcessingFailures);
                Assert.True(failure.IsSagaNotFound, failure.ToString());

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                Assert.Null(await run.FindSagaAsync<ReservationSaga>(correlationId, timeout.Token));
            });

        Assert.Empty(runs.Runs);
    }

    [Theory]
    [MatrixData(HostsCounts = [1, 2])]
    public async Task a_message_for_a_saga_that_is_over_is_ignored(string combination, int hostsCount)
    {
        const int messagesOfTheReservation = 2;

        var correlationId = Guid.NewGuid().ToString();
        var runs = new HandlerRuns(messagesOfTheReservation);
        var deliveries = new Deliveries();

        var setup = new HostSetup(bus => bus.AddSaga<ReservationSaga>())
        {
            ConfigureServices = services => services.AddSingleton(runs).CountDeliveries(deliveries)
        };

        await RunScenarioAsync(combination, hostsCount, setup,
            async (run, cancellationToken) =>
            {
                await run.AnyHost.PublishAsync(new ReserveSeat(correlationId), cancellationToken);
                await run.WaitForSagaAsync<ReservationSaga>(correlationId,
                    saga => saga.ProcessedMessages.Count == 1 && !saga.IsLocked, cancellationToken);

                await run.AnyHost.PublishAsync(new ConfirmReservation(correlationId), cancellationToken);
                await run.WaitForSagaAsync<ReservationSaga>(correlationId,
                    saga => saga.IsCompleted && !saga.IsLocked, cancellationToken);

                // a late cancellation, and the message that started the reservation, sent again
                await run.AnyHost.PublishAsync(new CancelReservation(correlationId), cancellationToken);
                await run.AnyHost.PublishAsync(new ReserveSeat(correlationId), cancellationToken);

                await deliveries.WaitUntilProcessedAsync<CancelReservation>(1, cancellationToken);
                await deliveries.WaitUntilProcessedAsync<ReserveSeat>(2, cancellationToken);

                // the saga is as it was when it was over, and it was not started again
                var stored = await run.FindSagaAsync<ReservationSaga>(correlationId, cancellationToken);
                Assert.NotNull(stored);
                Assert.True(stored.IsCompleted);
                Assert.False(stored.IsLocked);
                Assert.Equal(messagesOfTheReservation, stored.ProcessedMessages.Count);
            },
            async run =>
            {
                // ignored is not failed: no host gave a message back, and none was dead-lettered
                await AssertNoDeadLettersAsync(run);
                Assert.Empty(run.ProcessingFailures);
            });

        Assert.Empty(runs.RunsOf<CancelReservation>());
        Assert.Single(runs.RunsOf<ReserveSeat>());
        Assert.Single(runs.RunsOf<ConfirmReservation>());
        Assert.Single(runs.Runs.Select(run => run.InstanceId).Distinct());
    }
}
